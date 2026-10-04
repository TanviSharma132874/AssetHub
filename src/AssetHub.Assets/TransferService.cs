using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Assets;

public class TransferService
{
    private readonly AssetsDb _db;
    private readonly IdentityClient _identity;

    public TransferService(AssetsDb db, IdentityClient identity)
    {
        _db = db;
        _identity = identity;
    }

    public async Task<List<TransferDto>> ListAsync(CurrentUser me, string? status)
    {
        var q = _db.TransferRequests.AsNoTracking().Include(t => t.Asset).AsQueryable();

        if (!me.IsManager)
            q = q.Where(t => t.RequestedById == me.Id || t.FromEmployeeId == me.Id || t.ToEmployeeId == me.Id);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = Guard.ParseEnum<TransferStatus>(status, "Status");
            q = q.Where(t => t.Status == s);
        }

        var rows = await q.OrderByDescending(t => t.RequestedAt).Take(500).ToListAsync();
        return rows.Select(t => TransferDto.From(t, t.Asset)).ToList();
    }

    public async Task<TransferDto> CreateAsync(TransferCreateRequest req, CurrentUser by)
    {
        var target = await _identity.GetUserAsync(req.ToEmployeeId)
                     ?? throw new BadRequestException("Target employee not found.");
        if (!target.IsActive) throw new BadRequestException("Target employee's account is inactive.");
        var reason = Guard.OptionalText(req.Reason, "Reason", 500);

        return await WriteGate.RunAsync(async () =>
        {
            var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == req.AssetId)
                        ?? throw new NotFoundException("Asset not found.");

            var current = await _db.Allocations.FirstOrDefaultAsync(a => a.AssetId == asset.Id && a.ReturnedAt == null)
                          ?? throw new BadRequestException("Only assets that are currently allocated can be transferred.");

            if (!by.IsManager && current.EmployeeId != by.Id)
                throw new ForbiddenException("You can only request a transfer for an asset allocated to you.");
            if (current.EmployeeId == target.Id)
                throw new BadRequestException("The asset is already allocated to that employee.");
            if (await _db.TransferRequests.AnyAsync(t => t.AssetId == asset.Id && t.Status == TransferStatus.Pending))
                throw new ConflictException("A transfer request for this asset is already pending.");

            var transfer = new TransferRequest
            {
                AssetId = asset.Id,
                FromEmployeeId = current.EmployeeId,
                FromEmployeeName = current.EmployeeName,
                ToEmployeeId = target.Id,
                ToEmployeeName = target.FullName,
                ToEmployeeDepartment = target.DepartmentName,
                Reason = reason,
                RequestedById = by.Id,
                RequestedByName = by.Name
            };

            _db.TransferRequests.Add(transfer);
            await _db.SaveChangesAsync();
            return TransferDto.From(transfer, asset);
        });
    }

    /// <summary>Approving closes the current allocation and opens a new one for the target employee, atomically.</summary>
    public Task<TransferDto> ApproveAsync(int id, string? note, CurrentUser by) => WriteGate.RunAsync(async () =>
    {
        var transfer = await LoadPending(id);

        var current = await _db.Allocations.FirstOrDefaultAsync(a => a.AssetId == transfer.AssetId && a.ReturnedAt == null);
        if (current == null || current.EmployeeId != transfer.FromEmployeeId)
            throw new ConflictException("The asset's allocation changed after this request was made. Reject it and create a new request.");

        var now = DateTime.UtcNow;

        // Two saves inside one transaction: the old allocation must be closed before the new one is
        // inserted, otherwise the "one active allocation per asset" index would reject the insert.
        await using var tx = await _db.Database.BeginTransactionAsync();

        current.ReturnedAt = now;
        current.ReturnNotes = $"Transferred to {transfer.ToEmployeeName} (request #{transfer.Id})";
        await _db.SaveChangesAsync();

        _db.Allocations.Add(new Allocation
        {
            AssetId = transfer.AssetId,
            EmployeeId = transfer.ToEmployeeId,
            EmployeeName = transfer.ToEmployeeName,
            EmployeeDepartment = transfer.ToEmployeeDepartment,
            AllocatedAt = now,
            Notes = $"Transfer from {transfer.FromEmployeeName} (request #{transfer.Id})",
            AllocatedById = by.Id,
            AllocatedByName = by.Name
        });

        Decide(transfer, TransferStatus.Approved, note, by, now);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return TransferDto.From(transfer, transfer.Asset);
    });

    public Task<TransferDto> RejectAsync(int id, string? note, CurrentUser by) => WriteGate.RunAsync(async () =>
    {
        var transfer = await LoadPending(id);
        Decide(transfer, TransferStatus.Rejected, note, by, DateTime.UtcNow);
        await _db.SaveChangesAsync();
        return TransferDto.From(transfer, transfer.Asset);
    });

    public Task<TransferDto> CancelAsync(int id, CurrentUser by) => WriteGate.RunAsync(async () =>
    {
        var transfer = await LoadPending(id);
        if (!by.IsManager && transfer.RequestedById != by.Id)
            throw new ForbiddenException("Only the requester or a manager can cancel this request.");

        Decide(transfer, TransferStatus.Cancelled, null, by, DateTime.UtcNow);
        await _db.SaveChangesAsync();
        return TransferDto.From(transfer, transfer.Asset);
    });

    private async Task<TransferRequest> LoadPending(int id)
    {
        var transfer = await _db.TransferRequests.Include(t => t.Asset).FirstOrDefaultAsync(t => t.Id == id)
                       ?? throw new NotFoundException("Transfer request not found.");
        if (transfer.Status != TransferStatus.Pending)
            throw new ConflictException($"This request is already {transfer.Status.ToString().ToLowerInvariant()}.");
        return transfer;
    }

    private static void Decide(TransferRequest t, TransferStatus status, string? note, CurrentUser by, DateTime now)
    {
        t.Status = status;
        t.DecidedById = by.Id;
        t.DecidedByName = by.Name;
        t.DecidedAt = now;
        t.DecisionNote = Guard.OptionalText(note, "Note", 500);
    }
}
