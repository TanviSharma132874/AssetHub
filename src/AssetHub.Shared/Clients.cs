using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Shared;

// Subset of the Identity module's JSON, as seen by the other modules.
public record UserInfo(
    int Id, string FullName, string Email, string Role,
    int? DepartmentId, string? DepartmentName, string? Designation, bool IsActive);

public record CategoryInfo(int Id, string Name, string Prefix);

/// <summary>Server-to-server client for Module A (Identity). Forwards the caller's JWT.</summary>
public class IdentityClient
{
    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _accessor;

    public IdentityClient(HttpClient http, IHttpContextAccessor accessor)
    {
        _http = http;
        _accessor = accessor;
    }

    public Task<UserInfo?> GetUserAsync(int id, CancellationToken ct = default) =>
        UpstreamHttp.GetJsonAsync<UserInfo>(_http, _accessor, $"api/users/{id}", "Identity", ct);

    public Task<CategoryInfo?> GetCategoryAsync(int id, CancellationToken ct = default) =>
        UpstreamHttp.GetJsonAsync<CategoryInfo>(_http, _accessor, $"api/categories/{id}", "Identity", ct);
}

public static class ClientExtensions
{
    public static IServiceCollection AddIdentityClient(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient<IdentityClient>(c =>
        {
            c.BaseAddress = new Uri(config["Services:Identity"] ?? "http://localhost:5001");
            c.Timeout = TimeSpan.FromSeconds(10);
        });
        return services;
    }
}
