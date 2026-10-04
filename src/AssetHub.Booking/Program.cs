using System.Text.Json.Serialization;
using AssetHub.Booking;
using AssetHub.Shared;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BookingDb>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=booking.db"));

builder.Services.AddAssetHubAuth(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<AssetsClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Services:Assets"] ?? "http://localhost:5002");
    c.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddScoped<Notifier>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddHostedService<BookingReminderService>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BookingDb>();
    db.Database.EnsureCreated();

    if (!db.Resources.Any())
    {
        db.Resources.AddRange(
            new Resource { Name = "Conference Room A", Type = "Room", Location = "Floor 2", Capacity = 12, Description = "Projector and whiteboard" },
            new Resource { Name = "Training Room", Type = "Room", Location = "Floor 1", Capacity = 30 },
            new Resource { Name = "Portable Projector", Type = "Equipment", Location = "IT store" },
            new Resource { Name = "Pool Car", Type = "Vehicle", Location = "Basement parking", Capacity = 4 });
        db.SaveChanges();
    }
}

app.UseApiErrors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapBookingModule();

app.Run();
