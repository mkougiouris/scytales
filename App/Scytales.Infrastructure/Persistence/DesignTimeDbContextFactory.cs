using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Scytales.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without a host. Generating a migration never
/// connects, so the connection string is only a placeholder.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ScytalesDbContext>
{
    public ScytalesDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ScytalesDbContext>();
        ScytalesDbContext.Configure(builder, "Host=localhost;Database=scytales;Username=scytales;Password=scytales");
        return new ScytalesDbContext(builder.Options);
    }
}
