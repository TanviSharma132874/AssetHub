using AssetHub.Shared;
using Microsoft.AspNetCore.Identity;

namespace AssetHub.Identity;

public static class Seeder
{
    public static void Run(IdentityDb db, IPasswordHasher<User> hasher, IConfiguration config)
    {
        if (!db.Departments.Any())
        {
            db.Departments.AddRange(
                new Department { Name = "Information Technology", Code = "IT", Description = "IT and infrastructure" },
                new Department { Name = "Human Resources", Code = "HR", Description = "People operations" },
                new Department { Name = "Finance", Code = "FIN", Description = "Accounts and finance" },
                new Department { Name = "Operations", Code = "OPS", Description = "Day to day operations" });
        }

        if (!db.AssetCategories.Any())
        {
            db.AssetCategories.AddRange(
                new AssetCategory { Name = "Laptop", Prefix = "LAP", Description = "Laptops and notebooks" },
                new AssetCategory { Name = "Monitor", Prefix = "MON", Description = "Displays" },
                new AssetCategory { Name = "Mobile Phone", Prefix = "MOB", Description = "Phones and tablets" },
                new AssetCategory { Name = "Furniture", Prefix = "FUR", Description = "Desks, chairs, cabinets" },
                new AssetCategory { Name = "Vehicle", Prefix = "VEH", Description = "Company vehicles" },
                new AssetCategory { Name = "Network Equipment", Prefix = "NET", Description = "Routers, switches, access points" });
        }

        db.SaveChanges();

        if (!db.Users.Any())
        {
            var admin = new User
            {
                FullName = config["Seed:AdminName"] ?? "System Admin",
                Email = (config["Seed:AdminEmail"] ?? "admin@assethub.local").ToLowerInvariant(),
                Role = Roles.Admin,
                Designation = "Administrator",
                DepartmentId = db.Departments.Where(d => d.Code == "IT").Select(d => (int?)d.Id).FirstOrDefault()
            };
            admin.PasswordHash = hasher.HashPassword(admin, config["Seed:AdminPassword"] ?? "Admin@123");
            db.Users.Add(admin);
            db.SaveChanges();
        }
    }
}
