using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Achilles.Data;

namespace Achilles.ControlPlane.Tests;

public sealed class ControlPlaneFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=controlplane_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";
    private readonly SqliteConnection _keepAliveConnection;

    public ControlPlaneFactory()
    {
        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("Security:EnableDevSuperAdminBypass", "true");
        builder.UseSetting("Security:AllowLocalWebhooks", "true");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AchillesDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AchillesDbContext>(options =>
            {
                options.UseSqlite(_connectionString);
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _keepAliveConnection.Dispose();
        }
    }
}
