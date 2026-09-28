using Microsoft.EntityFrameworkCore;
using Tripgo.Domain.Interfaces;
using Tripgo.Infrastructure.Persistence;
using Tripgo.Infrastructure.Services;
using Tripgo.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// 1. Đăng ký DbContext với chuỗi kết nối từ appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// 2. Đăng ký TripService vào DI container
builder.Services.AddScoped<ITripService, TripService>();

// 3. Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 3. Register Customer Booking Service (Mock implementation for Sprint 1)
builder.Services.AddSingleton<Tripgo.Web.Services.ICustomerBookingService, Tripgo.Web.Services.CustomerBookingMockService>();

var app = builder.Build();

// 4. Tự động nạp dữ liệu mẫu khi khởi động (nếu có kết nối DB)
try
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<AppDbContext>();
        await context.Database.MigrateAsync();
        await DbInitializer.SeedAsync(context);
    }
}
catch (Exception ex)
{
    app.Logger.LogWarning("Không thể nạp dữ liệu mẫu CSDL khi khởi động: {Message}", ex.Message);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();