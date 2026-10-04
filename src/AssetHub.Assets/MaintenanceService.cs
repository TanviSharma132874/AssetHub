using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Assets;

/// <summary>
/// Maintenance flow:  Pending -> Approved (asset becomes UnderMaintenance) -> Completed (asset Available again,
/// or Retired if it could not be repaired).  A Pending request can also be Rejected.
/// </summary>
public class MaintenanceService
{
    private readonly AssetsDb _db;

    public MaintenanceService(AssetsDb db) => _db = db;

    public async Task<List<MaintenanceDto>> ListAsync(CurrentUser me, string? status)
    {
        var q = _db.MaintenanceRequests.AsNoTracking().Include(m => m.Asset).AsQueryable();

        if (!me.IsManager) q = q.Where(m => m.RequestedById == me.Id);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = Guard.ParseEnum<MaintenanceStatus>(status, "Status");
            q = q.Where(m => m.Status == s);
        }

        var rows = await q.OrderByDescending(m => m.RequestedAt).Take(500).ToListAsync();
        return rows.Select(m => MaintenanceDto.From(m, m.Asset)).ToList();
    }

    public Task<MaintenanceDto> CreateAsync(MaintenanceCreateRequest req, CurrentUser by) => WriteGate.RunAsync(async () =>
    {
        var issue = Guard.Text(req.Issue, "Issue description", 1000);
        if (!Enum.IsDefined(req.Priority)) throw new BadRequestException("Priority is not valid.");

        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == req.AssetId)
                    ?? throw new NotFoundException("Asset not found.");

        if (asset.Status == AssetStatus.Retired)
            throw new ConflictException("Retired assets cannot be sent for maintenance.");

        if (!by.IsManager)
        {
            var holds = await _db.Allocations.AnyAsync(a => a.AssetId == asset.Id && a.ReturnedAt == null && a.EmployeeId == by.Id);
            if (!holds) throw new ForbiddenException("You can only report issues for assets allocated to you.");
        }

        if (await _db.MaintenanceRequests.AnyAsync(m => m.AssetId == asset.Id
                && (m.Status == MaintenanceStatus.Pending || m.Status == MaintenanceStatus.Approved)))
            throw new ConflictException("There is already an open maintenance request for this asset.");

        var request = new MaintenanceRequest
        {
            AssetId = asset.Id,
            Issue = issue,
            Priority = req.Priority,
            RequestedById = by.Id,
            RequestedByName = by.Name
        };

        _db.MaintenanceRequests.Add(request);
        await _db.SaveChangesAsync();
        return MaintenanceDto.From(request, asset);
    });

    public Task<MaintenanceDto> ApproveAsync(int id, string? note, CurrentUser by) => WriteGate.RunAsync(async () =>
    {
        var request = await Load(id, MaintenanceStatus.Pending);

        if (request.Asset.Status == AssetStatus.Retired)
            throw new ConflictException("This asset has been retired in the meantime.");

        var now = DateTime.UtcNow;

        // The asset leaves its holder while it is being repaired.
        var active = await _db.Allocations.FirstOrDefaultAsync(a => a.AssetId == request.AssetId && a.ReturnedAt == null);
        if (active != null)
        {
            active.ReturnedAt = now;
            active.ReturnNotes = $"Returned for maintenance (request #{request.Id})";
        }

        request.Asset.Status = AssetStatus.UnderMaintenance;
        request.Asset.UpdatedAt = now;
        Decide(request, MaintenanceStatus.Approved, note, by, now);

        await _db.SaveChangesAsync();
        return MaintenanceDto.From(request, request.Asset);
    });

    public Task<MaintenanceDto> RejectAsync(int id, string? note, CurrentUser by) => WriteGate.RunAsync(async () =>
    {
        var request = await Load(id, MaintenanceStatus.Pending);
        Decide(request, MaintenanceStatus.Rejected, note, by, DateTime.UtcNow);
        await _db.SaveChangesAsync();
        return MaintenanceDto.From(request, request.Asset);
    });

    public Task<MaintenanceDto> CompleteAsync(int id, MaintenanceCompleteRequest? req, CurrentUser by) => WriteGate.RunAsync(async () =>
    {
        var request = await Load(id, MaintenanceStatus.Approved);
        var now = DateTime.UtcNow;

        request.Status = MaintenanceStatus.Completed;
        request.CompletedAt = now;
        request.CompletionNote = Guard.OptionalText(req?.Note, "Note", 500);

        request.Asset.Status = req?.Retire == true ? AssetStatus.Retired : AssetStatus.Available;
        request.Asset.UpdatedAt = now;

        await _db.SaveChangesAsync();
        return MaintenanceDto.From(request, request.Asset);
    });

    private async Task<MaintenanceRequest> Load(int id, MaintenanceStatus expected)
    {
        var request = await _db.MaintenanceRequests.Include(m => m.Asset).FirstOrDefaultAsync(m => m.Id == id)
                      ?? throw new NotFoundException("Maintenance request not found.");
        if (request.Status != expected)
            throw new ConflictException($"This request is {request.Status.ToString().ToLowerInvariant()}, so that action is not possible.");
        return request;
    }

    private static void Decide(MaintenanceRequest m, MaintenanceStatus status, string? note, CurrentUser by, DateTime now)
    {
        m.Status = status;
        m.DecidedById = by.Id;
        m.DecidedByName = by.Name;
        m.DecidedAt = now;
        m.DecisionNote = Guard.OptionalText(note, "Note", 500);
    }
}
