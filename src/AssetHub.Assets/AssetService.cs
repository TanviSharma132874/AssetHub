using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Assets;

public class AssetService
{
    private readonly AssetsDb _db;
    private readonly IdentityClient _identity;

    public AssetService(AssetsDb db, IdentityClient identity)
    {
        _db = db;
        _identity = identity;
    }

    public async Task<List<AssetDto>> ListAsync(string? status, int? categoryId, string? search, bool excludeRetired)
    {
        var q = _db.Assets.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = Guard.ParseEnum<AssetStatus>(status, "Status");
            q = q.Where(a => a.Status == s);
        }
        if (categoryId.HasValue) q = q.Where(a => a.CategoryId == categoryId.Value);
        if (excludeRetired) q = q.Where(a => a.Status != AssetStatus.Retired);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = $"%{search.Trim()}%";
            q = q.Where(a => EF.Functions.Like(a.Name, like)
                          || EF.Functions.Like(a.AssetTag, like)
                          || (a.SerialNumber != null && EF.Functions.Like(a.SerialNumber, like)));
        }

        var assets = await q.OrderBy(a => a.AssetTag).Take(1000).ToListAsync();
        var active = await _db.Allocations.AsNoTracking()
            .Where(a => a.ReturnedAt == null)
            .ToDictionaryAsync(a => a.AssetId);

        return assets.Select(a => AssetDto.From(a, active.TryGetValue(a.Id, out var al) ? al : null)).ToList();
    }

    public async Task<AssetDetailDto> GetAsync(int id)
    {
        var asset = await _db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id)
                    ?? throw new NotFoundException("Asset not found.");

        var history = await _db.Allocations.AsNoTracking()
            .Where(a => a.AssetId == id).OrderByDescending(a => a.AllocatedAt).ToListAsync();
        var maintenance = await _db.MaintenanceRequests.AsNoTracking()
            .Where(m => m.AssetId == id).OrderByDescending(m => m.RequestedAt).ToListAsync();

        var active = history.FirstOrDefault(h => h.ReturnedAt == null);
        return new AssetDetailDto(
            AssetDto.From(asset, active),
            history.Select(h => AllocationDto.From(h, asset)).ToList(),
            maintenance.Select(m => MaintenanceDto.From(m, asset)).ToList());
    }

    /// <summary>Registers an asset and gives it the next tag for its category, e.g. LAP-0007.</summary>
    public async Task<AssetDto> RegisterAsync(RegisterAssetRequest req, CurrentUser by)
    {
        var name = Guard.Text(req.Name, "Name", 150);
        var serial = Guard.OptionalText(req.SerialNumber, "Serial number", 100);
        var location = Guard.OptionalText(req.Location, "Location", 150);
        var notes = Guard.OptionalText(req.Notes, "Notes", 500);
        if (req.PurchaseCost is < 0) throw new BadRequestException("Purchase cost cannot be negative.");

        var category = await _identity.GetCategoryAsync(req.CategoryId)
                       ?? throw new BadRequestException("Selected category does not exist.");

        return await WriteGate.RunAsync(async () =>
        {
            if (serial != null && await _db.Assets.AnyAsync(a => a.SerialNumber == serial))
                throw new ConflictException("An asset with this serial number is already registered.");

            var counter = await _db.TagCounters.FirstOrDefaultAsync(c => c.Prefix == category.Prefix);
            if (counter == null)
            {
                counter = new TagCounter { Prefix = category.Prefix };
                _db.TagCounters.Add(counter);
            }
            counter.LastNumber++;

            var asset = new Asset
            {
                AssetTag = $"{category.Prefix}-{counter.LastNumber:D4}",
                Name = name,
                CategoryId = category.Id,
                CategoryName = category.Name,
                CategoryPrefix = category.Prefix,
                SerialNumber = serial,
                PurchaseDate = req.PurchaseDate,
                PurchaseCost = req.PurchaseCost,
                Location = location,
                Notes = notes,
                Status = AssetStatus.Available,
                CreatedById = by.Id
            };

            _db.Assets.Add(asset);
            await _db.SaveChangesAsync();
            return AssetDto.From(asset, null);
        });
    }

    public async Task<AssetDto> UpdateAsync(int id, UpdateAssetRequest req)
    {
        var name = Guard.Text(req.Name, "Name", 150);
        var serial = Guard.OptionalText(req.SerialNumber, "Serial number", 100);
        if (req.PurchaseCost is < 0) throw new BadRequestException("Purchase cost cannot be negative.");

        return await WriteGate.RunAsync(async () =>
        {
            var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id)
                        ?? throw new NotFoundException("Asset not found.");

            if (serial != null && await _db.Assets.AnyAsync(a => a.Id != id && a.SerialNumber == serial))
                throw new ConflictException("Another asset already has this serial number.");

            asset.Name = name;
            asset.SerialNumber = serial;
            asset.PurchaseDate = req.PurchaseDate;
            asset.PurchaseCost = req.PurchaseCost;
            asset.Location = Guard.OptionalText(req.Location, "Location", 150);
            asset.Notes = Guard.OptionalText(req.Notes, "Notes", 500);
            asset.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var active = await _db.Allocations.AsNoTracking().FirstOrDefaultAsync(a => a.AssetId == id && a.ReturnedAt == null);
            return AssetDto.From(asset, active);
        });
    }

    public Task<AssetDto> RetireAsync(int id) => WriteGate.RunAsync(async () =>
    {
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id)
                    ?? throw new NotFoundException("Asset not found.");

        if (asset.Status == AssetStatus.Retired) throw new ConflictException("This asset is already retired.");
        if (asset.Status == AssetStatus.Allocated) throw new ConflictException("Return the asset before retiring it.");

        asset.Status = AssetStatus.Retired;
        asset.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return AssetDto.From(asset, null);
    });

    public async Task<DashboardDto> DashboardAsync(CurrentUser me)
    {
        var groups = await _db.Assets.GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();

        var byStatus = Enum.GetNames<AssetStatus>().ToDictionary(n => n, _ => 0);
        foreach (var g in groups) byStatus[g.Status.ToString()] = g.Count;

        return new DashboardDto(
            byStatus,
            byStatus.Values.Sum(),
            await _db.TransferRequests.CountAsync(t => t.Status == TransferStatus.Pending),
            await _db.MaintenanceRequests.CountAsync(m => m.Status == MaintenanceStatus.Pending),
            await _db.Allocations.CountAsync(a => a.EmployeeId == me.Id && a.ReturnedAt == null));
    }
}
