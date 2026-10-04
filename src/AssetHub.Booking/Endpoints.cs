using System.Security.Claims;
using AssetHub.Shared;

namespace AssetHub.Booking;

public static class Endpoints
{
    private const string Manager = AuthExtensions.ManagerPolicy;

    public static void MapBookingModule(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { service = "booking", status = "ok" }));

        app.MapGet("/api/summary", async (ClaimsPrincipal user, BookingService svc) =>
            Results.Ok(await svc.SummaryAsync(user.Current()))).RequireAuthorization();

        MapResources(app.MapGroup("/api/resources").RequireAuthorization());
        MapBookings(app.MapGroup("/api/bookings").RequireAuthorization());
        MapAudits(app.MapGroup("/api/audits").RequireAuthorization());
        MapNotifications(app.MapGroup("/api/notifications").RequireAuthorization());
    }

    private static void MapResources(RouteGroupBuilder g)
    {
        g.MapGet("/", async (ClaimsPrincipal user, BookingService svc, bool? includeInactive) =>
            Results.Ok(await svc.ListResourcesAsync(includeInactive == true && user.Current().IsManager)));

        g.MapPost("/", async (ResourceRequest req, BookingService svc) =>
        {
            var created = await svc.CreateResourceAsync(req);
            return Results.Created($"/api/resources/{created.Id}", created);
        }).RequireAuthorization(Manager);

        g.MapPut("/{id:int}", async (int id, ResourceRequest req, BookingService svc) =>
            Results.Ok(await svc.UpdateResourceAsync(id, req))).RequireAuthorization(Manager);
    }

    private static void MapBookings(RouteGroupBuilder g)
    {
        // Query string: ?resourceId=&start=&end=&mine=&includeCancelled=   (start/end are ISO date-times)
        g.MapGet("/", async (ClaimsPrincipal user, BookingService svc,
                int? resourceId, DateTime? start, DateTime? end, bool? mine, bool? includeCancelled) =>
            Results.Ok(await svc.ListBookingsAsync(user.Current(), resourceId, start, end, mine == true, includeCancelled == true)));

        g.MapPost("/", async (BookingRequest req, ClaimsPrincipal user, BookingService svc) =>
        {
            var created = await svc.BookAsync(req, user.Current());
            return Results.Created($"/api/bookings/{created.Id}", created);
        });

        g.MapPost("/{id:int}/cancel", async (int id, ClaimsPrincipal user, BookingService svc) =>
            Results.Ok(await svc.CancelAsync(id, user.Current())));
    }

    private static void MapAudits(RouteGroupBuilder g)
    {
        g.MapGet("/", async (AuditService svc) => Results.Ok(await svc.ListAsync()));

        g.MapGet("/{id:int}", async (int id, AuditService svc) => Results.Ok(await svc.GetAsync(id)));

        g.MapPost("/", async (AuditCreateRequest req, ClaimsPrincipal user, AuditService svc) =>
        {
            var created = await svc.CreateAsync(req, user.Current());
            return Results.Created($"/api/audits/{created.Id}", created);
        }).RequireAuthorization(Manager);

        g.MapPut("/{id:int}/items/{itemId:int}", async (int id, int itemId, AuditVerifyRequest req, ClaimsPrincipal user, AuditService svc) =>
            Results.Ok(await svc.VerifyAsync(id, itemId, req, user.Current()))).RequireAuthorization(Manager);

        g.MapPost("/{id:int}/close", async (int id, AuditCloseRequest? req, ClaimsPrincipal user, AuditService svc) =>
            Results.Ok(await svc.CloseAsync(id, req?.Force == true, user.Current()))).RequireAuthorization(Manager);
    }

    private static void MapNotifications(RouteGroupBuilder g)
    {
        g.MapGet("/", async (ClaimsPrincipal user, Notifier notifier, bool? unreadOnly) =>
            Results.Ok(await notifier.ListAsync(user.UserId(), unreadOnly == true)));

        g.MapGet("/unread-count", async (ClaimsPrincipal user, Notifier notifier) =>
            Results.Ok(new { count = await notifier.UnreadCountAsync(user.UserId()) }));

        g.MapPost("/{id:int}/read", async (int id, ClaimsPrincipal user, Notifier notifier) =>
        {
            await notifier.MarkReadAsync(id, user.UserId());
            return Results.NoContent();
        });

        g.MapPost("/read-all", async (ClaimsPrincipal user, Notifier notifier) =>
        {
            await notifier.MarkAllReadAsync(user.UserId());
            return Results.NoContent();
        });
    }
}
