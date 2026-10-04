using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace AssetHub.Shared;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Employee = "Employee";

    public static readonly string[] All = { Employee, Manager, Admin };

    public static bool IsValid(string? role) => role != null && All.Contains(role);
}

public class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "AssetHub.Identity";
    public string Audience { get; set; } = "AssetHub";

    /// <summary>Must be identical in all three modules. At least 32 characters.</summary>
    public string Key { get; set; } = "";

    public int ExpiryMinutes { get; set; } = 480;
}

/// <summary>The signed-in user, read from JWT claims.</summary>
public record CurrentUser(int Id, string Name, string Role)
{
    public bool IsManager => Role == Roles.Admin || Role == Roles.Manager;
    public bool IsAdmin => Role == Roles.Admin;
}

public static class ClaimsExtensions
{
    public static int UserId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value;
        if (!int.TryParse(raw, out var id))
            throw new ApiException(401, "Invalid token: missing user id.");
        return id;
    }

    public static CurrentUser Current(this ClaimsPrincipal user) =>
        new(user.UserId(),
            user.FindFirst("name")?.Value ?? "Unknown",
            user.FindFirst("role")?.Value ?? Roles.Employee);
}

public static class AuthExtensions
{
    public const string AdminPolicy = "Admin";
    public const string ManagerPolicy = "Manager";

    /// <summary>
    /// Registers JWT bearer validation. All modules share the same Jwt:Key, so a token
    /// issued by the Identity module is accepted by the Asset and Booking modules.
    /// </summary>
    public static IServiceCollection AddAssetHubAuth(this IServiceCollection services, IConfiguration config)
    {
        var opts = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(opts.Key) || opts.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key must be configured and at least 32 characters long.");

        services.AddSingleton(opts);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false; // keep short claim names: sub, name, role, email
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = opts.Issuer,
                    ValidateAudience = true,
                    ValidAudience = opts.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
            });

        services.AddAuthorization(o =>
        {
            o.AddPolicy(AdminPolicy, p => p.RequireRole(Roles.Admin));
            o.AddPolicy(ManagerPolicy, p => p.RequireRole(Roles.Admin, Roles.Manager));
        });

        return services;
    }
}
