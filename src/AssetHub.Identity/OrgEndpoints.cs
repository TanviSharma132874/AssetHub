using System.Text.RegularExpressions;
using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Identity;

public static class OrgEndpoints
{
    private static readonly Regex PrefixRx = new("^[A-Z]{2,5}$", RegexOptions.Compiled);

    public static void MapOrganization(this IEndpointRouteBuilder app)
    {
        MapDepartments(app.MapGroup("/api/departments"));
        MapCategories(app.MapGroup("/api/categories").RequireAuthorization());
    }

    // ------------------------------------------------------------ departments
    private static void MapDepartments(RouteGroupBuilder g)
    {
        // Public: the sign-up form needs the list before the user has an account.
        g.MapGet("/", async (IdentityDb db) =>
            Results.Ok(await db.Departments.AsNoTracking().OrderBy(d => d.Name)
                .Select(d => new DepartmentDto(d.Id, d.Name, d.Code, d.Description, d.Users.Count))
                .ToListAsync()));

        g.MapPost("/", async (DepartmentRequest req, IdentityDb db) =>
        {
            var name = Guard.Text(req.Name, "Name", 100);
            var code = Guard.Text(req.Code, "Code", 10).ToUpperInvariant();

            if (await db.Departments.AnyAsync(d => d.Name == name)) throw new ConflictException("A department with this name already exists.");
            if (await db.Departments.AnyAsync(d => d.Code == code)) throw new ConflictException("A department with this code already exists.");

            var dept = new Department { Name = name, Code = code, Description = Guard.OptionalText(req.Description, "Description", 300) };
            db.Departments.Add(dept);
            await db.SaveChangesAsync();
            return Results.Created($"/api/departments/{dept.Id}", new DepartmentDto(dept.Id, dept.Name, dept.Code, dept.Description, 0));
        }).RequireAuthorization(AuthExtensions.AdminPolicy);

        g.MapPut("/{id:int}", async (int id, DepartmentRequest req, IdentityDb db) =>
        {
            var dept = await db.Departments.FindAsync(id) ?? throw new NotFoundException("Department not found.");
            var name = Guard.Text(req.Name, "Name", 100);
            var code = Guard.Text(req.Code, "Code", 10).ToUpperInvariant();

            if (await db.Departments.AnyAsync(d => d.Id != id && d.Name == name)) throw new ConflictException("A department with this name already exists.");
            if (await db.Departments.AnyAsync(d => d.Id != id && d.Code == code)) throw new ConflictException("A department with this code already exists.");

            dept.Name = name;
            dept.Code = code;
            dept.Description = Guard.OptionalText(req.Description, "Description", 300);
            await db.SaveChangesAsync();

            var count = await db.Users.CountAsync(u => u.DepartmentId == id);
            return Results.Ok(new DepartmentDto(dept.Id, dept.Name, dept.Code, dept.Description, count));
        }).RequireAuthorization(AuthExtensions.AdminPolicy);

        g.MapDelete("/{id:int}", async (int id, IdentityDb db) =>
        {
            var dept = await db.Departments.FindAsync(id) ?? throw new NotFoundException("Department not found.");
            if (await db.Users.AnyAsync(u => u.DepartmentId == id))
                throw new ConflictException("This department still has employees. Move them to another department first.");
            db.Departments.Remove(dept);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization(AuthExtensions.AdminPolicy);
    }

    // ------------------------------------------------------------ asset categories
    private static void MapCategories(RouteGroupBuilder g)
    {
        g.MapGet("/", async (IdentityDb db) =>
            Results.Ok(await db.AssetCategories.AsNoTracking().OrderBy(c => c.Name)
                .Select(c => new CategoryDto(c.Id, c.Name, c.Prefix, c.Description)).ToListAsync()));

        g.MapGet("/{id:int}", async (int id, IdentityDb db) =>
        {
            var c = await db.AssetCategories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
                    ?? throw new NotFoundException("Category not found.");
            return Results.Ok(CategoryDto.From(c));
        });

        g.MapPost("/", async (CategoryRequest req, IdentityDb db) =>
        {
            var name = Guard.Text(req.Name, "Name", 100);
            var prefix = Guard.Text(req.Prefix, "Prefix", 5).ToUpperInvariant();
            if (!PrefixRx.IsMatch(prefix)) throw new BadRequestException("Prefix must be 2 to 5 letters (A-Z).");

            if (await db.AssetCategories.AnyAsync(c => c.Name == name)) throw new ConflictException("A category with this name already exists.");
            if (await db.AssetCategories.AnyAsync(c => c.Prefix == prefix)) throw new ConflictException("A category with this tag prefix already exists.");

            var cat = new AssetCategory { Name = name, Prefix = prefix, Description = Guard.OptionalText(req.Description, "Description", 300) };
            db.AssetCategories.Add(cat);
            await db.SaveChangesAsync();
            return Results.Created($"/api/categories/{cat.Id}", CategoryDto.From(cat));
        }).RequireAuthorization(AuthExtensions.AdminPolicy);

        // The prefix is immutable: asset tags that already carry it would stop matching their category.
        g.MapPut("/{id:int}", async (int id, CategoryRequest req, IdentityDb db) =>
        {
            var cat = await db.AssetCategories.FindAsync(id) ?? throw new NotFoundException("Category not found.");
            var name = Guard.Text(req.Name, "Name", 100);
            if (await db.AssetCategories.AnyAsync(c => c.Id != id && c.Name == name)) throw new ConflictException("A category with this name already exists.");

            cat.Name = name;
            cat.Description = Guard.OptionalText(req.Description, "Description", 300);
            await db.SaveChangesAsync();
            return Results.Ok(CategoryDto.From(cat));
        }).RequireAuthorization(AuthExtensions.AdminPolicy);

        g.MapDelete("/{id:int}", async (int id, IdentityDb db) =>
        {
            var cat = await db.AssetCategories.FindAsync(id) ?? throw new NotFoundException("Category not found.");
            db.AssetCategories.Remove(cat);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization(AuthExtensions.AdminPolicy);
    }
}
