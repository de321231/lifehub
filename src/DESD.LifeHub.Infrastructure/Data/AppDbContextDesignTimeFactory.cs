using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DESD.LifeHub.Infrastructure.Data;

/// <summary>
/// Design-Time-Factory für <c>dotnet ef</c>. Migrationen benötigen keine laufende
/// Datenbank; die Verbindungszeichenfolge ist daher nur ein Platzhalter und kann
/// über die Umgebungsvariable <c>ConnectionStrings__Postgres</c> überschrieben werden.
/// </summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=desd_lifehub;Username=desd_lifehub;Password=design-time-only";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
