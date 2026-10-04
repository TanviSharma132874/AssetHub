using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Identity;

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Description { get; set; }
    public List<User> Users { get; set; } = new();
}

public class AssetCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>2-5 uppercase letters. Module B builds asset tags from it, e.g. LAP-0001.</summary>
    public string Prefix { get; set; } = "";
    public string? Description { get; set; }
}

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Employee;
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public string? Designation { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class IdentityDb : DbContext
{
    public IdentityDb(DbContextOptions<IdentityDb> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.FullName).HasMaxLength(100);
            e.HasOne(x => x.Department).WithMany(d => d.Users)
                .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Department>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<AssetCategory>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.Prefix).IsUnique();
        });

        b.ApplyUtcDateTimes();
    }
}
