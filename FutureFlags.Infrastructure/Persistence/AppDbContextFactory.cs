using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FutureFlags.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core CLI (`dotnet ef migrations add`). At runtime the context is
/// configured by Aspire's Npgsql client integration, which supplies the real connection string.
/// This connection string is never opened — it only has to parse so the model can be built.
/// </summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=futureflagsdb;Username=postgres;Password=postgres";

    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(DesignTimeConnectionString)
            .Options;

        return new AppDbContext(options);
    }
}
