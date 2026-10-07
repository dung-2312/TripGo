using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tripgo.Application;
using Tripgo.Infrastructure;
using Tripgo.Infrastructure.Persistence;
using Tripgo.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// 1. Clean Architecture Modules
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 2. Add UI & Razor Components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 3. Register Customer Booking Service (Mock implementation for Sprint 1)
builder.Services.AddSingleton<Tripgo.Web.Services.ICustomerBookingService, Tripgo.Web.Services.CustomerBookingMockService>();

var app = builder.Build();

// 4. Auto-migration & Database Seeding on startup
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
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();