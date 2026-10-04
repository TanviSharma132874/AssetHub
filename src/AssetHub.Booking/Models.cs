using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Booking;

public enum BookingStatus { Confirmed, Cancelled }
public enum AuditStatus { Open, Closed }
public enum AuditItemStatus { Pending, Verified, Missing, Damaged }

public class Resource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Room";       // Room, Vehicle, Equipment, ...
    public string? Location { get; set; }
    public int? Capacity { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Booking
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;

    public int BookedById { get; set; }
    public string BookedByName { get; set; } = "";
    public string Title { get; set; } = "";

    public DateTime StartTime { get; set; }   // UTC
    public DateTime EndTime { get; set; }     // UTC
    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;
    public bool ReminderSent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAt { get; set; }
}

public class AuditCycle
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int? CategoryFilterId { get; set; }
    public string? CategoryFilterName { get; set; }
    public DateTime? DueDate { get; set; }
    public AuditStatus Status { get; set; } = AuditStatus.Open;

    public int CreatedById { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    public List<AuditItem> Items { get; set; } = new();
}

/// <summary>One asset to verify in a cycle. Asset details are a snapshot taken when the cycle was created.</summary>
public class AuditItem
{
    public int Id { get; set; }
    public int AuditCycleId { get; set; }
    public AuditCycle AuditCycle { get; set; } = null!;

    public int AssetId { get; set; }
    public string AssetTag { get; set; } = "";
    public string AssetName { get; set; } = "";
    public string? CategoryName { get; set; }
    public string? Location { get; set; }

    public AuditItemStatus Status { get; set; } = AuditItemStatus.Pending;
    public string? Remarks { get; set; }
    public int? VerifiedById { get; set; }
    public string? VerifiedByName { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Type { get; set; } = "Info";   // Booking, Audit, Reminder
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class BookingDb : DbContext
{
    public BookingDb(DbContextOptions<BookingDb> options) : base(options) { }

    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<AuditCycle> AuditCycles => Set<AuditCycle>();
    public DbSet<AuditItem> AuditItems => Set<AuditItem>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void ConfigureConventions(ModelConfigurationBuilder cb)
    {
        cb.Properties<BookingStatus>().HaveConversion<string>();
        cb.Properties<AuditStatus>().HaveConversion<string>();
        cb.Properties<AuditItemStatus>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Resource>(e => e.HasIndex(x => x.Name).IsUnique());

        b.Entity<Booking>(e =>
        {
            e.HasOne(x => x.Resource).WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ResourceId, x.StartTime, x.EndTime });
            e.HasIndex(x => x.BookedById);
        });

        b.Entity<AuditCycle>(e =>
            e.HasMany(x => x.Items).WithOne(i => i.AuditCycle).HasForeignKey(i => i.AuditCycleId).OnDelete(DeleteBehavior.Cascade));

        b.Entity<AuditItem>(e => e.HasIndex(x => new { x.AuditCycleId, x.AssetId }).IsUnique());

        b.Entity<Notification>(e => e.HasIndex(x => new { x.UserId, x.IsRead }));

        b.ApplyUtcDateTimes();
    }
}
