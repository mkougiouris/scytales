using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Scytales.Infrastructure.Persistence;

public static class DatabaseMigration
{
    /// <summary>
    /// Applies pending migrations. Fine for the local sample; in production this belongs in a separate
    /// deployment step (RL-7). EF Core takes a database lock, so concurrent callers don't collide.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ScytalesDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
