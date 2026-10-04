using System.Security.Claims;
using AssetHub.Shared;

namespace AssetHub.Assets;

public static class Endpoints
{
    private const string Manager = AuthExtensions.ManagerPolicy;

    public static void MapAssetModule(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { service = "assets", status = "ok" }));

        app.MapGet("/api/dashboard", async (ClaimsPrincipal user, AssetService svc) =>
            Results.Ok(await svc.DashboardAsync(user.Current()))).RequireAuthorization();

        MapAssets(app.MapGroup("/api/assets").RequireAuthorization());
        MapAllocations(app.MapGroup("/api/allocations").RequireAuthorization());
        MapTransfers(app.MapGroup("/api/transfers").RequireAuthorization());
        MapMaintenance(app.MapGroup("/api/maintenance").RequireAuthorization());
    }

    private static void MapAssets(RouteGroupBuilder g)
    {
        g.MapGet("/", async (AssetService svc, string? status, int? categoryId, string? search, bool? excludeRetired) =>
            Results.Ok(await svc.ListAsync(status, categoryId, search, excludeRetired == true)));

        g.MapGet("/{id:int}", async (int id, AssetService svc) => Results.Ok(await svc.GetAsync(id)));

        g.MapPost("/", async (RegisterAssetRequest req, ClaimsPrincipal user, AssetService svc) =>
        {
            var created = await svc.RegisterAsync(req, user.Current());
            return Results.Created($"/api/assets/{created.Id}", created);
        }).RequireAuthorization(Manager);

        g.MapPut("/{id:int}", async (int id, UpdateAssetRequest req, AssetService svc) =>
            Results.Ok(await svc.UpdateAsync(id, req))).RequireAuthorization(Manager);

        g.MapPost("/{id:int}/retire", async (int id, AssetService svc) =>
            Results.Ok(await svc.RetireAsync(id))).RequireAuthorization(Manager);
    }

    private static void MapAllocations(RouteGroupBuilder g)
    {
        g.MapGet("/", async (ClaimsPrincipal user, AllocationService svc, bool? active, int? employeeId) =>
            Results.Ok(await svc.ListAsync(user.Current(), active, employeeId)));

        g.MapGet("/mine", async (ClaimsPrincipal user, AllocationService svc) =>
        {
            var me = user.Current();
            return Results.Ok(await svc.ListAsync(me, true, me.Id));
        });

        g.MapPost("/", async (AllocateRequest req, ClaimsPrincipal user, AllocationService svc) =>
        {
            var created = await svc.AllocateAsync(req, user.Current());
            return Results.Created($"/api/allocations/{created.Id}", created);
        }).RequireAuthorization(Manager);

        g.MapPost("/{id:int}/return", async (int id, ReturnRequest? req, ClaimsPrincipal user, AllocationService svc) =>
            Results.Ok(await svc.ReturnAsync(id, req, user.Current())));
    }

    private static void MapTransfers(RouteGroupBuilder g)
    {
        g.MapGet("/", async (ClaimsPrincipal user, TransferService svc, string? status) =>
            Results.Ok(await svc.ListAsync(user.Current(), status)));

        g.MapPost("/", async (TransferCreateRequest req, ClaimsPrincipal user, TransferService svc) =>
        {
            var created = await svc.CreateAsync(req, user.Current());
            return Results.Created($"/api/transfers/{created.Id}", created);
        });

        g.MapPost("/{id:int}/approve", async (int id, DecisionRequest? req, ClaimsPrincipal user, TransferService svc) =>
            Results.Ok(await svc.ApproveAsync(id, req?.Note, user.Current()))).RequireAuthorization(Manager);

        g.MapPost("/{id:int}/reject", async (int id, DecisionRequest? req, ClaimsPrincipal user, TransferService svc) =>
            Results.Ok(await svc.RejectAsync(id, req?.Note, user.Current()))).RequireAuthorization(Manager);

        g.MapPost("/{id:int}/cancel", async (int id, ClaimsPrincipal user, TransferService svc) =>
            Results.Ok(await svc.CancelAsync(id, user.Current())));
    }

    private static void MapMaintenance(RouteGroupBuilder g)
    {
        g.MapGet("/", async (ClaimsPrincipal user, MaintenanceService svc, string? status) =>
            Results.Ok(await svc.ListAsync(user.Current(), status)));

        g.MapPost("/", async (MaintenanceCreateRequest req, ClaimsPrincipal user, MaintenanceService svc) =>
        {
            var created = await svc.CreateAsync(req, user.Current());
            return Results.Created($"/api/maintenance/{created.Id}", created);
        });

        g.MapPost("/{id:int}/approve", async (int id, DecisionRequest? req, ClaimsPrincipal user, MaintenanceService svc) =>
            Results.Ok(await svc.ApproveAsync(id, req?.Note, user.Current()))).RequireAuthorization(Manager);

        g.MapPost("/{id:int}/reject", async (int id, DecisionRequest? req, ClaimsPrincipal user, MaintenanceService svc) =>
            Results.Ok(await svc.RejectAsync(id, req?.Note, user.Current()))).RequireAuthorization(Manager);

        g.MapPost("/{id:int}/complete", async (int id, MaintenanceCompleteRequest? req, ClaimsPrincipal user, MaintenanceService svc) =>
            Results.Ok(await svc.CompleteAsync(id, req, user.Current()))).RequireAuthorization(Manager);
    }
}
