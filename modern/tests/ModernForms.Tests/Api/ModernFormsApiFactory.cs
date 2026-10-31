using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ModernForms.Tests.Api;

/// <summary>
/// Contract-test fixture. The application is provider-aware
/// (Database:Provider), so the factory pins the SQLite demo profile with a
/// fresh per-fixture database file — no DbContext registrations are
/// overridden, avoiding the classic "two EF providers in one host" failure.
/// Program's DatabaseInitializer guarantees schema + demo data before
/// traffic, exactly like the deployed container starts.
/// </summary>
public sealed class ModernFormsApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"modernforms-{Guid.NewGuid():N}.db");

        builder.UseEnvironment("Development");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={databasePath}");
    }
}
