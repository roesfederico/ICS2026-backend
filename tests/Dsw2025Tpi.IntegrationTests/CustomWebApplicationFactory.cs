using Dsw2025Tpi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dsw2025Tpi.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Api.Program>, IAsyncLifetime
{
    private readonly SqliteConnection _appConnection;
    private readonly SqliteConnection _authConnection;

    public CustomWebApplicationFactory()
    {
        // Conexión SQLite en memoria compartida para Dsw2025TpiContext
        _appConnection = new SqliteConnection("DataSource=AppTestDb;Mode=Memory;Cache=Shared");
        _appConnection.Open();

        // Conexión SQLite en memoria compartida para AuthenticateContext
        _authConnection = new SqliteConnection("DataSource=AuthTestDb;Mode=Memory;Cache=Shared");
        _authConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // 1. Limpiar por completo la configuración previa (SQL Server) de ambos contextos
            services.RemoveAll(typeof(DbContextOptions<Dsw2025TpiContext>));
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<Dsw2025TpiContext>));

            services.RemoveAll(typeof(DbContextOptions<AuthenticateContext>));
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AuthenticateContext>));

            // 2. Registrar SQLite en memoria compartida
            services.AddDbContext<Dsw2025TpiContext>(options =>
            {
                options.UseSqlite(_appConnection);
            });

            services.AddDbContext<AuthenticateContext>(options =>
            {
                options.UseSqlite(_authConnection);
            });

            // 3. Crear las tablas en ambas bases SQLite en memoria
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var authDb = scope.ServiceProvider.GetRequiredService<AuthenticateContext>();
            authDb.Database.EnsureCreated();

            var appDb = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            appDb.Database.EnsureCreated();
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        await _appConnection.DisposeAsync();
        await _authConnection.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("Integration Tests")]
public class IntegrationTestCollection : ICollectionFixture<CustomWebApplicationFactory>
{
}