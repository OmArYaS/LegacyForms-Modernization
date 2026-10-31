using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModernForms.Application.Common;
using ModernForms.Infrastructure.Common;
using ModernForms.Infrastructure.Persistence;
using ModernForms.Infrastructure.Security;

namespace ModernForms.Infrastructure;

/// <summary>
/// Persistence worker. Provider selection follows Database:Provider in
/// configuration — exactly the same knob the legacy-era deployment uses for
/// its MySQL profile; the modern API runs Sqlite locally for demos, Azure
/// SQL/SqlServer for cloud deployments and MySQL keeps its stored-procedure
/// pathway documented in docs/migration-strategy.md.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = (configuration["Database:Provider"] ?? "Sqlite").Trim();

        services.AddDbContext<FormsDbContext>(options => _ = provider.ToLowerInvariant() switch
        {
            "sqlite" => options.UseSqlite(configuration.GetConnectionString("Sqlite")
                ?? "Data Source=forms.db"),
            "sqlserver" or "azuresql" => options.UseSqlServer(
                configuration.GetConnectionString("SqlServer")
                ?? throw new InvalidOperationException("Database connection 'SqlServer' is not configured.")),
            _ => throw new InvalidOperationException(
                $"Database:Provider '{provider}' is not supported. Use Sqlite, SqlServer or AzureSql.")
        });

        services.AddScoped<IFormsDbContext>(sp => sp.GetRequiredService<FormsDbContext>());
        services.AddScoped<DatabaseInitializer>();

        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IDateTime, DateTimeProvider>();

        return services;
    }

    /// <summary>Runs migrations + demo seed on startup (dev only; production uses Azure DevOps release jobs).</summary>
    public static async Task UseSeedDemoDataAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync();
    }
}
