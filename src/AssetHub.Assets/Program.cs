using System.Text.Json.Serialization;
using AssetHub.Assets;
using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AssetsDb>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=assets.db"));

builder.Services.AddAssetHubAuth(builder.Configuration);
builder.Services.AddIdentityClient(builder.Configuration);

builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<AllocationService>();
builder.Services.AddScoped<TransferService>();
builder.Services.AddScoped<MaintenanceService>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AssetsDb>().Database.EnsureCreated();
}

app.UseApiErrors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapAssetModule();

app.Run();
