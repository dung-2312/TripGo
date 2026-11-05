using Microsoft.EntityFrameworkCore;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence;

/// <summary>
/// Seeds only neutral reference vocabulary: provinces/cities and generic bus classes.
/// Operators, routes, fares and schedules are deliberately not seeded here because they are
/// real-world claims that need evidence; see docs/data/transport-data-quality.md.
/// </summary>
public static class DbInitializer
{
    public static async Task SeedAsync(AppDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.EnsureWritesTargetIsolatedTestDatabase();

        foreach (var (code, name, province) in new[]
        {
            ("HAN", "Hà Nội", "Hà Nội"),
            ("SGN", "TP. Hồ Chí Minh", "Hồ Chí Minh"),
            ("DLI", "Đà Lạt", "Lâm Đồng"),
            ("NHA", "Nha Trang", "Khánh Hòa"),
            ("UIH", "Quy Nhơn", "Bình Định"),
            ("SAP", "Sa Pa", "Lào Cai"),
            ("DAD", "Đà Nẵng", "Đà Nẵng")
        })
        {
            if (!await context.Locations.AnyAsync(location => location.Code == code))
            {
                context.Locations.Add(new Location
                {
                    Code = code,
                    Name = name,
                    Province = province,
                    Country = "Vietnam",
                    IsActive = true
                });
            }
        }

        foreach (var (name, description) in new[]
        {
            ("Xe giường nằm tiêu chuẩn", "Giường nằm tiêu chuẩn đường dài"),
            ("Limousine giường phòng", "Limousine phòng đơn hai tầng"),
            ("Limousine phòng đôi", "Limousine phòng đôi hai tầng")
        })
        {
            if (!await context.BusTypes.AnyAsync(type => type.Name == name))
            {
                context.BusTypes.Add(new BusType { Name = name, Description = description, IsActive = true });
            }
        }

        await context.SaveChangesAsync();
    }
}
