using DairyBidding.IdentityService.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DairyBidding.IdentityService.Extensions;

public static class DatabaseExtensions
{
    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<IdentityDbContext>>();

        await db.Database.MigrateAsync();

        if (!await db.Users.AnyAsync())
        {
            var adminPassword = config["Identity:AdminPassword"]
                ?? throw new InvalidOperationException("Identity:AdminPassword is required. Set it via dotnet user-secrets.");

            var admin = new User
            {
                Username = "admin",
                Role = "Admin",
                PasswordHash = string.Empty
            };
            admin.PasswordHash = hasher.HashPassword(admin, adminPassword);

            db.Users.Add(admin);
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded admin user.");
        }
    }
}
