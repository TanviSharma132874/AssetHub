using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AssetHub.Shared;

// ---------- Errors: thrown anywhere, turned into { "error": "..." } by the middleware ----------

public class ApiException : Exception
{
    public int Status { get; }
    public ApiException(int status, string message) : base(message) => Status = status;
}

public class BadRequestException : ApiException
{
    public BadRequestException(string message) : base(400, message) { }
}

public class ForbiddenException : ApiException
{
    public ForbiddenException(string message) : base(403, message) { }
}

public class NotFoundException : ApiException
{
    public NotFoundException(string message) : base(404, message) { }
}

public class ConflictException : ApiException
{
    public ConflictException(string message) : base(409, message) { }
}

public class UpstreamException : ApiException
{
    public UpstreamException(string message) : base(503, message) { }
}

public class ApiErrorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiErrorMiddleware> _log;

    public ApiErrorMiddleware(RequestDelegate next, ILogger<ApiErrorMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task Invoke(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (ApiException ex) when (!ctx.Response.HasStarted)
        {
            await Write(ctx, ex.Status, ex.Message);
        }
        catch (BadHttpRequestException) when (!ctx.Response.HasStarted)
        {
            await Write(ctx, 400, "The request body is missing or invalid.");
        }
        catch (Exception ex) when (!ctx.Response.HasStarted)
        {
            _log.LogError(ex, "Unhandled error on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            await Write(ctx, 500, "Something went wrong on the server.");
        }
    }

    private static Task Write(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error = message });
    }
}

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseApiErrors(this IApplicationBuilder app) =>
        app.UseMiddleware<ApiErrorMiddleware>();
}

// ---------- Input validation helpers ----------

public static class Guard
{
    private static readonly Regex EmailRx =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public static string Text(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new BadRequestException($"{field} is required.");
        if (v.Length > max) throw new BadRequestException($"{field} must be at most {max} characters.");
        return v;
    }

    public static string? OptionalText(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw new BadRequestException($"{field} must be at most {max} characters.");
        return v;
    }

    public static string Email(string? value)
    {
        var v = Text(value, "Email", 200).ToLowerInvariant();
        if (!EmailRx.IsMatch(v)) throw new BadRequestException("Email address is not valid.");
        return v;
    }

    public static void Password(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 8)
            throw new BadRequestException("Password must be at least 8 characters.");
        if (value.Length > 100)
            throw new BadRequestException("Password must be at most 100 characters.");
        if (!value.Any(char.IsLetter) || !value.Any(char.IsDigit))
            throw new BadRequestException("Password must contain at least one letter and one digit.");
    }

    public static T ParseEnum<T>(string? value, string field) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, true, out var r) && Enum.IsDefined(r)) return r;
        throw new BadRequestException($"{field} is not valid.");
    }
}

// ---------- Calling another module (forwarding the caller's bearer token) ----------

public static class UpstreamHttp
{
    public static async Task<T?> GetJsonAsync<T>(
        HttpClient http, IHttpContextAccessor accessor, string url, string serviceName,
        CancellationToken ct = default) where T : class
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);

        var auth = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth))
            req.Headers.TryAddWithoutValidation("Authorization", auth);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct);
        }
        catch (HttpRequestException)
        {
            throw new UpstreamException($"The {serviceName} service is not reachable. Make sure it is running.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new UpstreamException($"The {serviceName} service timed out.");
        }

        using (res)
        {
            if (res.StatusCode == HttpStatusCode.NotFound) return null;
            if (!res.IsSuccessStatusCode)
                throw new UpstreamException($"The {serviceName} service returned an error ({(int)res.StatusCode}).");
            return await res.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }
    }
}
