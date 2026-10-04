using System.Security.Claims;
using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Identity;

public static class UserEndpoints
{
    public static void MapUsers(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/users").RequireAuthorization();

        // Employee directory
        g.MapGet("/", async (IdentityDb db, string? search, int? departmentId, string? role, bool? includeInactive) =>
        {
            var q = db.Users.AsNoTracking().Include(u => u.Department).AsQueryable();

            if (includeInactive != true) q = q.Where(u => u.IsActive);
            if (departmentId.HasValue) q = q.Where(u => u.DepartmentId == departmentId);
            if (!string.IsNullOrWhiteSpace(role)) q = q.Where(u => u.Role == role);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var like = $"%{search.Trim()}%";
                q = q.Where(u => EF.Functions.Like(u.FullName, like)
                              || EF.Functions.Like(u.Email, like)
                              || (u.Designation != null && EF.Functions.Like(u.Designation, like)));
            }

            var users = await q.OrderBy(u => u.FullName).Take(500).ToListAsync();
            return Results.Ok(users.Select(UserDto.From));
        });

        // Also used by the other modules to validate employee ids.
        g.MapGet("/{id:int}", async (int id, IdentityDb db) =>
        {
            var user = await db.Users.AsNoTracking().Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == id)
                       ?? throw new NotFoundException("Employee not found.");
            return Results.Ok(UserDto.From(user));
        });

        g.MapPut("/{id:int}", async (int id, UpdateUserRequest req, ClaimsPrincipal me, IdentityDb db) =>
        {
            var user = await db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == id)
                       ?? throw new NotFoundException("Employee not found.");

            if (req.DepartmentId is int deptId && !await db.Departments.AnyAsync(d => d.Id == deptId))
                throw new BadRequestException("Selected department does not exist.");

            if (!req.IsActive && user.IsActive)
            {
                if (user.Id == me.UserId())
                    throw new BadRequestException("You cannot deactivate your own account.");
                if (user.Role == Roles.Admin && await db.Users.CountAsync(u => u.Role == Roles.Admin && u.IsActive) <= 1)
                    throw new ConflictException("At least one active admin is required.");
            }

            user.FullName = Guard.Text(req.FullName, "Full name", 100);
            user.DepartmentId = req.DepartmentId;
            user.Designation = Guard.OptionalText(req.Designation, "Designation", 100);
            user.Phone = Guard.OptionalText(req.Phone, "Phone", 30);
            user.IsActive = req.IsActive;
            await db.SaveChangesAsync();

            await db.Entry(user).Reference(u => u.Department).LoadAsync();
            return Results.Ok(UserDto.From(user));
        }).RequireAuthorization(AuthExtensions.AdminPolicy);

        // Admin-only role promotion / demotion. The affected user must sign in again to get a token with the new role.
        g.MapPut("/{id:int}/role", async (int id, RoleChangeRequest req, ClaimsPrincipal me, IdentityDb db) =>
        {
            if (!Roles.IsValid(req.Role))
                throw new BadRequestException($"Role must be one of: {string.Join(", ", Roles.All)}.");

            var user = await db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == id)
                       ?? throw new NotFoundException("Employee not found.");

            if (user.Id == me.UserId())
                throw new BadRequestException("You cannot change your own role.");

            if (user.Role == Roles.Admin && req.Role != Roles.Admin
                && await db.Users.CountAsync(u => u.Role == Roles.Admin && u.IsActive) <= 1)
                throw new ConflictException("At least one active admin is required.");

            user.Role = req.Role;
            await db.SaveChangesAsync();
            return Results.Ok(UserDto.From(user));
        }).RequireAuthorization(AuthExtensions.AdminPolicy);
    }
}
