using System.Text.Json.Serialization;
using AssetHub.Identity;
using AssetHub.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<IdentityDb>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=identity.db"));

builder.Services.AddAssetHubAuth(builder.Configuration);
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<JwtTokenService>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The UIs of the other modules run on other ports and call this API directly (sign-in, dropdown data).
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IdentityDb>();
    db.Database.EnsureCreated();
    Seeder.Run(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(), app.Configuration);
}

app.UseApiErrors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { service = "identity", status = "ok" }));
app.MapAuth();
app.MapUsers();
app.MapOrganization();

app.Run();
