using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Assets;

public enum AssetStatus { Available, Allocated, UnderMaintenance, Retired }
public enum TransferStatus { Pending, Approved, Rejected, Cancelled }
public enum MaintenancePriority { Low, Medium, High, Critical }
public enum MaintenanceStatus { Pending, Approved, Rejected, Completed }

public class Asset
{
    public int Id { get; set; }

    /// <summary>Auto-generated, e.g. LAP-0001. Never edited.</summary>
    public string AssetTag { get; set; } = "";
    public string Name { get; set; } = "";

    // Category data lives in the Identity module; we keep a snapshot so this module keeps working on its own.
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public string CategoryPrefix { get; set; } = "";

    public string? SerialNumber { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public double? PurchaseCost { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Available;
    public int CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Last number handed out per category prefix.</summary>
public class TagCounter
{
    public string Prefix { get; set; } = "";
    public int LastNumber { get; set; }
}

public class Allocation
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public Asset Asset { get; set; } = null!;

    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string? EmployeeDepartment { get; set; }

    public DateTime AllocatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedReturnDate { get; set; }
    public DateTime? ReturnedAt { get; set; }   // null = still allocated
    public string? Notes { get; set; }
    public string? ReturnNotes { get; set; }

    public int AllocatedById { get; set; }
    public string AllocatedByName { get; set; } = "";
}

public class TransferRequest
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public Asset Asset { get; set; } = null!;

    public int FromEmployeeId { get; set; }
    public string FromEmployeeName { get; set; } = "";
    public int ToEmployeeId { get; set; }
    public string ToEmployeeName { get; set; } = "";
    public string? ToEmployeeDepartment { get; set; }

    public string? Reason { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Pending;

    public int RequestedById { get; set; }
    public string RequestedByName { get; set; } = "";
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public int? DecidedById { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
}

public class MaintenanceRequest
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public Asset Asset { get; set; } = null!;

    public string Issue { get; set; } = "";
    public MaintenancePriority Priority { get; set; } = MaintenancePriority.Medium;
    public MaintenanceStatus Status { get; set; } = MaintenanceStatus.Pending;

    public int RequestedById { get; set; }
    public string RequestedByName { get; set; } = "";
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public int? DecidedById { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    public DateTime? CompletedAt { get; set; }
    public string? CompletionNote { get; set; }
}

public class AssetsDb : DbContext
{
    public AssetsDb(DbContextOptions<AssetsDb> options) : base(options) { }

    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<TagCounter> TagCounters => Set<TagCounter>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<TransferRequest> TransferRequests => Set<TransferRequest>();
    public DbSet<MaintenanceRequest> MaintenanceRequests => Set<MaintenanceRequest>();

    protected override void ConfigureConventions(ModelConfigurationBuilder cb)
    {
        cb.Properties<AssetStatus>().HaveConversion<string>();
        cb.Properties<TransferStatus>().HaveConversion<string>();
        cb.Properties<MaintenancePriority>().HaveConversion<string>();
        cb.Properties<MaintenanceStatus>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Asset>(e =>
        {
            e.HasIndex(x => x.AssetTag).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.AssetTag).HasMaxLength(20);
        });

        b.Entity<TagCounter>(e => e.HasKey(x => x.Prefix));

        b.Entity<Allocation>(e =>
        {
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.EmployeeId);

            // Database-level guarantee against double allocation: at most ONE row per asset
            // may have ReturnedAt = NULL (i.e. be currently active).
            e.HasIndex(x => x.AssetId).IsUnique().HasFilter("\"ReturnedAt\" IS NULL");
        });

        b.Entity<TransferRequest>(e =>
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict));

        b.Entity<MaintenanceRequest>(e =>
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict));

        b.ApplyUtcDateTimes();
    }
}
