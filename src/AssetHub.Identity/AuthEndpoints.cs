using System.Security.Claims;
using AssetHub.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Identity;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        // Anyone can sign up; new accounts are always Employees. Only an Admin can promote them.
        g.MapPost("/signup", async (SignupRequest req, IdentityDb db, IPasswordHasher<User> hasher, JwtTokenService jwt) =>
        {
            var name = Guard.Text(req.FullName, "Full name", 100);
            var email = Guard.Email(req.Email);
            Guard.Password(req.Password);

            if (await db.Users.AnyAsync(u => u.Email == email))
                throw new ConflictException("An account with this email already exists.");

            if (req.DepartmentId is int deptId && !await db.Departments.AnyAsync(d => d.Id == deptId))
                throw new BadRequestException("Selected department does not exist.");

            var user = new User
            {
                FullName = name,
                Email = email,
                Role = Roles.Employee,
                DepartmentId = req.DepartmentId,
                Designation = Guard.OptionalText(req.Designation, "Designation", 100),
                Phone = Guard.OptionalText(req.Phone, "Phone", 30)
            };
            user.PasswordHash = hasher.HashPassword(user, req.Password);

            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                throw new ConflictException("An account with this email already exists.");
            }

            await db.Entry(user).Reference(u => u.Department).LoadAsync();
            var (token, expires) = jwt.Create(user);
            return Results.Created($"/api/users/{user.Id}", new AuthResponse(token, expires, UserDto.From(user)));
        });

        g.MapPost("/login", async (LoginRequest req, IdentityDb db, IPasswordHasher<User> hasher, JwtTokenService jwt) =>
        {
            var email = (req.Email ?? "").Trim().ToLowerInvariant();
            var user = await db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Email == email);

            // Same message for unknown email and wrong password so accounts cannot be probed.
            var invalid = new ApiException(401, "Invalid email or password.");
            if (user == null || !user.IsActive) throw invalid;

            var result = hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password ?? "");
            if (result == PasswordVerificationResult.Failed) throw invalid;

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = hasher.HashPassword(user, req.Password!);
                await db.SaveChangesAsync();
            }

            var (token, expires) = jwt.Create(user);
            return Results.Ok(new AuthResponse(token, expires, UserDto.From(user)));
        });

        g.MapGet("/me", async (ClaimsPrincipal principal, IdentityDb db) =>
        {
            var id = principal.UserId();
            var user = await db.Users.AsNoTracking().Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == id);
            if (user == null || !user.IsActive) throw new ApiException(401, "Account not found or disabled.");
            return Results.Ok(UserDto.From(user));
        }).RequireAuthorization();
    }
}
