using AssetHub.Shared;

namespace AssetHub.Booking;

/// <summary>Server-to-server client for Module B (Assets). Forwards the caller's JWT.</summary>
public class AssetsClient
{
    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _accessor;

    public AssetsClient(HttpClient http, IHttpContextAccessor accessor)
    {
        _http = http;
        _accessor = accessor;
    }

    public async Task<List<AssetInfo>> GetAuditableAssetsAsync(int? categoryId, CancellationToken ct = default)
    {
        var url = "api/assets?excludeRetired=true" + (categoryId.HasValue ? $"&categoryId={categoryId}" : "");
        var assets = await UpstreamHttp.GetJsonAsync<List<AssetInfo>>(_http, _accessor, url, "Assets", ct);
        return assets ?? new List<AssetInfo>();
    }
}
