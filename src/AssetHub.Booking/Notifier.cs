using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Booking;

/// <summary>
/// In-app notifications. <see cref="Add"/> only stages the row on the DbContext, so a notification is saved
/// in the same transaction as the change that caused it (a booking and its confirmation succeed or fail together).
/// </summary>
public class Notifier
{
    private readonly BookingDb _db;

    public Notifier(BookingDb db) => _db = db;

    public void Add(int userId, string title, string message, string type) =>
        _db.Notifications.Add(new Notification { UserId = userId, Title = title, Message = message, Type = type });

    public async Task<List<NotificationDto>> ListAsync(int userId, bool unreadOnly)
    {
        var q = _db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly) q = q.Where(n => !n.IsRead);
        var rows = await q.OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync();
        return rows.Select(NotificationDto.From).ToList();
    }

    public Task<int> UnreadCountAsync(int userId) =>
        _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

    public async Task MarkReadAsync(int id, int userId)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId)
                ?? throw new NotFoundException("Notification not found.");
        n.IsRead = true;
        await _db.SaveChangesAsync();
    }

    public async Task MarkAllReadAsync(int userId)
    {
        var unread = await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
        foreach (var n in unread) n.IsRead = true;
        await _db.SaveChangesAsync();
    }
}

/// <summary>Creates a reminder notification shortly before a confirmed booking starts.</summary>
public class BookingReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BookingReminderService> _log;
    private readonly int _minutesBefore;

    public BookingReminderService(IServiceScopeFactory scopes, IConfiguration config, ILogger<BookingReminderService> log)
    {
        _scopes = scopes;
        _log = log;
        _minutesBefore = config.GetValue("Booking:ReminderMinutesBefore", 30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BookingDb>();

                var now = DateTime.UtcNow;
                var horizon = now.AddMinutes(_minutesBefore);

                var due = await db.Bookings.Include(b => b.Resource)
                    .Where(b => b.Status == BookingStatus.Confirmed && !b.ReminderSent
                                && b.StartTime > now && b.StartTime <= horizon)
                    .ToListAsync(stoppingToken);

                foreach (var b in due)
                {
                    db.Notifications.Add(new Notification
                    {
                        UserId = b.BookedById,
                        Title = "Booking starts soon",
                        Message = $"\"{b.Title}\" on {b.Resource.Name} starts at {b.StartTime:HH:mm} UTC.",
                        Type = "Reminder"
                    });
                    b.ReminderSent = true;
                }

                if (due.Count > 0) await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Booking reminder run failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
