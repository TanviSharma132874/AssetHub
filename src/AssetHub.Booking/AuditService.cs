using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Booking;

public class AuditService
{
    private readonly BookingDb _db;
    private readonly AssetsClient _assets;
    private readonly Notifier _notifier;

    public AuditService(BookingDb db, AssetsClient assets, Notifier notifier)
    {
        _db = db;
        _assets = assets;
        _notifier = notifier;
    }

    public async Task<List<AuditCycleDto>> ListAsync()
    {
        var cycles = await _db.AuditCycles.AsNoTracking().OrderByDescending(c => c.CreatedAt).Take(200).ToListAsync();
        return await ToDtosAsync(cycles);
    }

    public async Task<AuditDetailDto> GetAsync(int id)
    {
        var cycle = await _db.AuditCycles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id)
                    ?? throw new NotFoundException("Audit cycle not found.");
        var items = await _db.AuditItems.AsNoTracking().Where(i => i.AuditCycleId == id).OrderBy(i => i.AssetTag).ToListAsync();
        var dto = (await ToDtosAsync(new List<AuditCycle> { cycle }))[0];
        return new AuditDetailDto(dto, items.Select(AuditItemDto.From).ToList());
    }

    /// <summary>Starts a cycle: takes a snapshot of every non-retired asset (optionally one category) to verify.</summary>
    public async Task<AuditCycleDto> CreateAsync(AuditCreateRequest req, CurrentUser by)
    {
        var name = Guard.Text(req.Name, "Name", 150);
        var description = Guard.OptionalText(req.Description, "Description", 500);
        if (req.DueDate is DateTime due && due.Date < DateTime.UtcNow.Date)
            throw new BadRequestException("Due date cannot be in the past.");

        var assets = await _assets.GetAuditableAssetsAsync(req.CategoryId);
        if (assets.Count == 0)
            throw new BadRequestException(req.CategoryId.HasValue
                ? "There are no assets in that category to audit."
                : "There are no assets to audit yet.");

        var cycle = new AuditCycle
        {
            Name = name,
            Description = description,
            CategoryFilterId = req.CategoryId,
            CategoryFilterName = req.CategoryId.HasValue ? assets[0].CategoryName : null,
            DueDate = req.DueDate,
            CreatedById = by.Id,
            CreatedByName = by.Name,
            Items = assets.Select(a => new AuditItem
            {
                AssetId = a.Id,
                AssetTag = a.AssetTag,
                AssetName = a.Name,
                CategoryName = a.CategoryName,
                Location = a.Location
            }).ToList()
        };

        _db.AuditCycles.Add(cycle);
        _notifier.Add(by.Id, "Audit cycle created", $"\"{name}\" is open with {assets.Count} asset(s) to verify.", "Audit");
        await _db.SaveChangesAsync();

        return (await ToDtosAsync(new List<AuditCycle> { cycle }))[0];
    }

    public async Task<AuditItemDto> VerifyAsync(int cycleId, int itemId, AuditVerifyRequest req, CurrentUser by)
    {
        var status = Guard.ParseEnum<AuditItemStatus>(req.Status, "Status");
        if (status == AuditItemStatus.Pending)
            throw new BadRequestException("Choose Verified, Missing or Damaged.");

        var remarks = Guard.OptionalText(req.Remarks, "Remarks", 500);
        if (status != AuditItemStatus.Verified && remarks == null)
            throw new BadRequestException($"Remarks are required when an asset is marked {status}.");

        var item = await _db.AuditItems.Include(i => i.AuditCycle)
                       .FirstOrDefaultAsync(i => i.Id == itemId && i.AuditCycleId == cycleId)
                   ?? throw new NotFoundException("Audit item not found.");

        if (item.AuditCycle.Status == AuditStatus.Closed)
            throw new ConflictException("This audit cycle is closed.");

        item.Status = status;
        item.Remarks = remarks;
        item.VerifiedById = by.Id;
        item.VerifiedByName = by.Name;
        item.VerifiedAt = DateTime.UtcNow;

        if (status != AuditItemStatus.Verified)
        {
            _notifier.Add(item.AuditCycle.CreatedById, $"Asset marked {status}",
                $"{item.AssetTag} ({item.AssetName}) was marked {status} in \"{item.AuditCycle.Name}\": {remarks}", "Audit");
        }

        await _db.SaveChangesAsync();
        return AuditItemDto.From(item);
    }

    public async Task<AuditCycleDto> CloseAsync(int id, bool force, CurrentUser by)
    {
        var cycle = await _db.AuditCycles.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == id)
                    ?? throw new NotFoundException("Audit cycle not found.");

        if (cycle.Status == AuditStatus.Closed) throw new ConflictException("This audit cycle is already closed.");

        var pending = cycle.Items.Count(i => i.Status == AuditItemStatus.Pending);
        if (pending > 0 && !force)
            throw new ConflictException($"{pending} asset(s) are still pending verification. Verify them first, or close anyway.");

        cycle.Status = AuditStatus.Closed;
        cycle.ClosedAt = DateTime.UtcNow;

        int Count(AuditItemStatus s) => cycle.Items.Count(i => i.Status == s);
        _notifier.Add(cycle.CreatedById, "Audit cycle closed",
            $"\"{cycle.Name}\" closed by {by.Name}: {Count(AuditItemStatus.Verified)} verified, " +
            $"{Count(AuditItemStatus.Missing)} missing, {Count(AuditItemStatus.Damaged)} damaged, {pending} not verified.", "Audit");

        await _db.SaveChangesAsync();
        return (await ToDtosAsync(new List<AuditCycle> { cycle }))[0];
    }

    private async Task<List<AuditCycleDto>> ToDtosAsync(List<AuditCycle> cycles)
    {
        var counts = await _db.AuditItems.AsNoTracking()
            .GroupBy(i => new { i.AuditCycleId, i.Status })
            .Select(g => new { g.Key.AuditCycleId, g.Key.Status, N = g.Count() })
            .ToListAsync();

        return cycles.Select(c =>
        {
            int Of(AuditItemStatus s) => counts.Where(x => x.AuditCycleId == c.Id && x.Status == s).Sum(x => x.N);
            var pending = Of(AuditItemStatus.Pending);
            var verified = Of(AuditItemStatus.Verified);
            var missing = Of(AuditItemStatus.Missing);
            var damaged = Of(AuditItemStatus.Damaged);

            return new AuditCycleDto(
                c.Id, c.Name, c.Description, c.CategoryFilterName, c.DueDate, c.Status,
                c.CreatedByName, c.CreatedAt, c.ClosedAt,
                pending + verified + missing + damaged, pending, verified, missing, damaged);
        }).ToList();
    }
}
