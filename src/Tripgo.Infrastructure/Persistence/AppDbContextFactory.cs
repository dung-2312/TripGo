using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tripgo.Infrastructure.Persistence;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // Đổi sang Trusted_Connection=True để dùng Windows Authentication
        optionsBuilder.UseSqlServer("Server=.\\SQLEXPRESS;Database=TripgoDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true;");

        return new AppDbContext(optionsBuilder.Options);
    }
}