using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Booking;

public class BookingService
{
    private readonly BookingDb _db;
    private readonly Notifier _notifier;
    private readonly int _maxHours;

    public BookingService(BookingDb db, Notifier notifier, IConfiguration config)
    {
        _db = db;
        _notifier = notifier;
        _maxHours = config.GetValue("Booking:MaxHours", 12);
    }

    // ------------------------------------------------------------ resources
    public async Task<List<ResourceDto>> ListResourcesAsync(bool includeInactive)
    {
        var q = _db.Resources.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(r => r.IsActive);
        var rows = await q.OrderBy(r => r.Type).ThenBy(r => r.Name).ToListAsync();
        return rows.Select(ResourceDto.From).ToList();
    }

    public async Task<ResourceDto> CreateResourceAsync(ResourceRequest req)
    {
        var name = Guard.Text(req.Name, "Name", 100);
        if (await _db.Resources.AnyAsync(r => r.Name == name))
            throw new ConflictException("A resource with this name already exists.");

        var resource = new Resource { Name = name };
        Apply(resource, req);
        _db.Resources.Add(resource);
        await _db.SaveChangesAsync();
        return ResourceDto.From(resource);
    }

    public async Task<ResourceDto> UpdateResourceAsync(int id, ResourceRequest req)
    {
        var resource = await _db.Resources.FirstOrDefaultAsync(r => r.Id == id)
                       ?? throw new NotFoundException("Resource not found.");
        var name = Guard.Text(req.Name, "Name", 100);
        if (await _db.Resources.AnyAsync(r => r.Id != id && r.Name == name))
            throw new ConflictException("A resource with this name already exists.");

        resource.Name = name;
        Apply(resource, req);
        await _db.SaveChangesAsync();
        return ResourceDto.From(resource);
    }

    private static void Apply(Resource r, ResourceRequest req)
    {
        if (req.Capacity is < 0) throw new BadRequestException("Capacity cannot be negative.");
        r.Type = Guard.Text(req.Type, "Type", 50);
        r.Location = Guard.OptionalText(req.Location, "Location", 150);
        r.Capacity = req.Capacity;
        r.Description = Guard.OptionalText(req.Description, "Description", 300);
        r.IsActive = req.IsActive;
    }

    // ------------------------------------------------------------ bookings
    public async Task<List<BookingDto>> ListBookingsAsync(
        CurrentUser me, int? resourceId, DateTime? windowStart, DateTime? windowEnd, bool mine, bool includeCancelled)
    {
        var q = _db.Bookings.AsNoTracking().Include(b => b.Resource).AsQueryable();

        if (resourceId.HasValue) q = q.Where(b => b.ResourceId == resourceId.Value);
        if (mine) q = q.Where(b => b.BookedById == me.Id);
        if (windowStart.HasValue) { var s = windowStart.Value.AsUtc(); q = q.Where(b => b.EndTime > s); }
        if (windowEnd.HasValue) { var e = windowEnd.Value.AsUtc(); q = q.Where(b => b.StartTime < e); }
        if (!includeCancelled) q = q.Where(b => b.Status == BookingStatus.Confirmed);

        var rows = await q.OrderBy(b => b.StartTime).Take(500).ToListAsync();
        return rows.Select(b => BookingDto.From(b, b.Resource)).ToList();
    }

    /// <summary>
    /// Creates a booking after validating the time range and checking for overlaps with other confirmed
    /// bookings of the same resource. Two ranges overlap when each starts before the other ends, so
    /// back-to-back bookings (10:00-11:00 then 11:00-12:00) are allowed.
    /// </summary>
    public async Task<BookingDto> BookAsync(BookingRequest req, CurrentUser by)
    {
        var title = Guard.Text(req.Title, "Title", 150);
        var start = req.StartTime.AsUtc();
        var end = req.EndTime.AsUtc();
        var now = DateTime.UtcNow;

        if (end <= start) throw new BadRequestException("End time must be after the start time.");
        if (start < now.AddMinutes(-1)) throw new BadRequestException("A booking cannot start in the past.");
        if ((end - start).TotalHours > _maxHours)
            throw new BadRequestException($"A booking can be at most {_maxHours} hours long.");

        return await WriteGate.RunAsync(async () =>
        {
            var resource = await _db.Resources.FirstOrDefaultAsync(r => r.Id == req.ResourceId)
                           ?? throw new NotFoundException("Resource not found.");
            if (!resource.IsActive)
                throw new ConflictException($"{resource.Name} is not available for booking.");

            var clash = await _db.Bookings
                .Where(b => b.ResourceId == resource.Id && b.Status == BookingStatus.Confirmed
                            && b.StartTime < end && b.EndTime > start)
                .OrderBy(b => b.StartTime)
                .FirstOrDefaultAsync();

            if (clash != null)
                throw new ConflictException(
                    $"{resource.Name} is already booked by {clash.BookedByName} from " +
                    $"{clash.StartTime:dd MMM HH:mm} to {clash.EndTime:HH:mm} (UTC). Pick a different time.");

            var booking = new Booking
            {
                ResourceId = resource.Id,
                BookedById = by.Id,
                BookedByName = by.Name,
                Title = title,
                StartTime = start,
                EndTime = end
            };

            _db.Bookings.Add(booking);
            _notifier.Add(by.Id, "Booking confirmed",
                $"{resource.Name} is booked for \"{title}\" on {start:dd MMM yyyy}, {start:HH:mm}-{end:HH:mm} UTC.", "Booking");

            await _db.SaveChangesAsync();
            return BookingDto.From(booking, resource);
        });
    }

    public Task<BookingDto> CancelAsync(int id, CurrentUser by) => WriteGate.RunAsync(async () =>
    {
        var booking = await _db.Bookings.Include(b => b.Resource).FirstOrDefaultAsync(b => b.Id == id)
                      ?? throw new NotFoundException("Booking not found.");

        if (booking.Status == BookingStatus.Cancelled) throw new ConflictException("This booking is already cancelled.");
        if (!by.IsManager && booking.BookedById != by.Id)
            throw new ForbiddenException("You can only cancel your own bookings.");
        if (booking.EndTime < DateTime.UtcNow) throw new ConflictException("Past bookings cannot be cancelled.");

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = DateTime.UtcNow;

        var byOther = booking.BookedById != by.Id ? $" by {by.Name}" : "";
        _notifier.Add(booking.BookedById, "Booking cancelled",
            $"Your booking \"{booking.Title}\" on {booking.Resource.Name} ({booking.StartTime:dd MMM HH:mm} UTC) was cancelled{byOther}.", "Booking");

        await _db.SaveChangesAsync();
        return BookingDto.From(booking, booking.Resource);
    });

    // ------------------------------------------------------------ summary
    public async Task<object> SummaryAsync(CurrentUser me)
    {
        var now = DateTime.UtcNow;
        var week = now.AddDays(7);
        return new
        {
            upcomingBookings = await _db.Bookings.CountAsync(b => b.BookedById == me.Id && b.Status == BookingStatus.Confirmed
                                                                  && b.EndTime > now && b.StartTime < week),
            openAudits = await _db.AuditCycles.CountAsync(c => c.Status == AuditStatus.Open),
            pendingAuditItems = await _db.AuditItems.CountAsync(i => i.Status == AuditItemStatus.Pending && i.AuditCycle.Status == AuditStatus.Open),
            unreadNotifications = await _notifier.UnreadCountAsync(me.Id)
        };
    }
}
