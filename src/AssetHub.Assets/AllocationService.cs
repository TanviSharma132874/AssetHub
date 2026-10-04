using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Assets;

public class AllocationService
{
    private readonly AssetsDb _db;
    private readonly IdentityClient _identity;

    public AllocationService(AssetsDb db, IdentityClient identity)
    {
        _db = db;
        _identity = identity;
    }

    public async Task<List<AllocationDto>> ListAsync(CurrentUser me, bool? activeOnly, int? employeeId)
    {
        var q = _db.Allocations.AsNoTracking().Include(a => a.Asset).AsQueryable();

        // Employees only ever see their own allocations.
        if (!me.IsManager) employeeId = me.Id;
        if (employeeId.HasValue) q = q.Where(a => a.EmployeeId == employeeId.Value);
        if (activeOnly == true) q = q.Where(a => a.ReturnedAt == null);

        var rows = await q.OrderByDescending(a => a.AllocatedAt).Take(500).ToListAsync();
        return rows.Select(a => AllocationDto.From(a, a.Asset)).ToList();
    }

    /// <summary>
    /// Allocates an asset to an employee. Conflict detection: an asset that is already allocated,
    /// under maintenance or retired is rejected. The check and the write run under one lock, and a
    /// filtered unique index on the table is the final safety net.
    /// </summary>
    public async Task<AllocationDto> AllocateAsync(AllocateRequest req, CurrentUser by)
    {
        var employee = await _identity.GetUserAsync(req.EmployeeId)
                       ?? throw new BadRequestException("Employee not found.");
        if (!employee.IsActive) throw new BadRequestException("This employee's account is inactive.");

        if (req.ExpectedReturnDate is DateTime due && due.Date < DateTime.UtcNow.Date)
            throw new BadRequestException("Expected return date cannot be in the past.");

        var notes = Guard.OptionalText(req.Notes, "Notes", 500);

        return await WriteGate.RunAsync(async () =>
        {
            var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == req.AssetId)
                        ?? throw new NotFoundException("Asset not found.");

            if (asset.Status == AssetStatus.Retired)
                throw new ConflictException($"{asset.AssetTag} is retired and cannot be allocated.");
            if (asset.Status == AssetStatus.UnderMaintenance)
                throw new ConflictException($"{asset.AssetTag} is under maintenance and cannot be allocated.");

            var current = await _db.Allocations.FirstOrDefaultAsync(a => a.AssetId == asset.Id && a.ReturnedAt == null);
            if (current != null || asset.Status == AssetStatus.Allocated)
                throw new ConflictException(
                    $"{asset.AssetTag} is already allocated" + (current != null ? $" to {current.EmployeeName}." : "."));

            var now = DateTime.UtcNow;
            var allocation = new Allocation
            {
                AssetId = asset.Id,
                EmployeeId = employee.Id,
                EmployeeName = employee.FullName,
                EmployeeDepartment = employee.DepartmentName,
                AllocatedAt = now,
                ExpectedReturnDate = req.ExpectedReturnDate,
                Notes = notes,
                AllocatedById = by.Id,
                AllocatedByName = by.Name
            };

            _db.Allocations.Add(allocation);
            asset.Status = AssetStatus.Allocated;
            asset.UpdatedAt = now;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                throw new ConflictException($"{asset.AssetTag} was just allocated by someone else.");
            }

            return AllocationDto.From(allocation, asset);
        });
    }

    public Task<AllocationDto> ReturnAsync(int id, ReturnRequest? req, CurrentUser by) => WriteGate.RunAsync(async () =>
    {
        var allocation = await _db.Allocations.Include(a => a.Asset).FirstOrDefaultAsync(a => a.Id == id)
                         ?? throw new NotFoundException("Allocation not found.");

        if (allocation.ReturnedAt != null) throw new ConflictException("This asset has already been returned.");
        if (!by.IsManager && allocation.EmployeeId != by.Id)
            throw new ForbiddenException("Only the current holder or a manager can return this asset.");

        var now = DateTime.UtcNow;
        allocation.ReturnedAt = now;
        allocation.ReturnNotes = Guard.OptionalText(req?.Notes, "Notes", 500);

        if (allocation.Asset.Status == AssetStatus.Allocated)
        {
            allocation.Asset.Status = AssetStatus.Available;
            allocation.Asset.UpdatedAt = now;
        }

        await _db.SaveChangesAsync();
        return AllocationDto.From(allocation, allocation.Asset);
    });
}
