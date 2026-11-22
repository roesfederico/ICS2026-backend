using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dsw2025Tpi.IntegrationTests;

[Collection("Integration Tests")]
public class ProductsControllerIntegrationTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProductsControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    // Limpiamos la tabla Products antes de cada test para que sean 100% independientes
    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var existing = await db.Set<Product>().ToListAsync();
        if (existing.Any())
        {
            db.Set<Product>().RemoveRange(existing);
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string GenerateJwtToken(string username, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        return jwtService.GenerateToken(username, role, "");
    }

    private void AuthenticateAs(string username, string role)
    {
        var token = GenerateJwtToken(username, role);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void ClearAuthentication()
    {
        _client.DefaultRequestHeaders.Authorization = null;
    }

    // =========================================================================
    // 1. Creación de productos (Admin)
    // =========================================================================

    [Fact]
    public async Task AddProduct_WhenUnauthenticatedOrNotAdmin_ReturnsUnauthorizedOrForbidden()
    {
        // Arrange
        var request = new ProductModel.Request(
            Sku: "SKU-AUTH-001",
            InternalCode: "INT-001",
            Name: "Teclado Mecánico",
            Description: "Switch Red",
            CurrentUnitPrice: 45000m,
            StockQuantity: 10
        );

        // Act 1: Petición anónima (sin token)
        ClearAuthentication();
        var unauthResponse = await _client.PostAsJsonAsync("/api/products", request);

        // Assert 1: Debe responder 401 Unauthorized
        unauthResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Act 2: Petición autenticada con rol "User" (no Admin)
        AuthenticateAs("cliente_test", "User");
        var forbiddenResponse = await _client.PostAsJsonAsync("/api/products", request);

        // Assert 2: Debe responder 403 Forbidden
        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AddProduct_WhenAuthenticatedAsAdmin_PersistsInSqliteAndReturnsCreated()
    {
        // Arrange
        AuthenticateAs("admin", "Admin");
        var request = new ProductModel.Request(
            Sku: "SKU-NEW-100",
            InternalCode: "INT-100",
            Name: "Monitor Gamer 27",
            Description: "144Hz IPS",
            CurrentUnitPrice: 320000m,
            StockQuantity: 15
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", request);

        // Assert HTTP
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdDto = await response.Content.ReadFromJsonAsync<ProductModel.ResponseProduct>(_jsonOptions);
        createdDto.Should().NotBeNull();
        createdDto!.Sku.Should().Be("SKU-NEW-100");
        createdDto.Name.Should().Be("Monitor Gamer 27");

        // Assert Persistencia real en SQLite
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var productInDb = await db.Set<Product>().FirstOrDefaultAsync(p => p.Sku == "SKU-NEW-100");

        productInDb.Should().NotBeNull();
        productInDb!.InternalCode.Should().Be("INT-100");
        productInDb.CurrentUnitPrice.Should().Be(320000m);
        productInDb.StockQuantity.Should().Be(15);
        productInDb.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AddProduct_WhenSkuAlreadyExistsInDatabase_ReturnsConflictStatusCode()
    {
        // Arrange: Pre-insertar producto en SQLite
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            db.Set<Product>().Add(new Product("SKU-DUP-01", "Mouse Inalámbrico", 25000m, 8, "Óptico", "INT-DUP-01"));
            await db.SaveChangesAsync();
        }

        AuthenticateAs("admin", "Admin");
        var duplicateRequest = new ProductModel.Request(
            Sku: "SKU-DUP-01",
            InternalCode: "INT-DUP-02",
            Name: "Otro Mouse",
            Description: "Duplicado",
            CurrentUnitPrice: 30000m,
            StockQuantity: 5
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/products", duplicateRequest);

        // Assert: Debe devolver 409 Conflict (o 400 BadRequest según ExceptionHandlingMiddleware, nunca 500)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
    }

    // =========================================================================
    // 2. Listado de productos
    // =========================================================================

    [Fact]
    public async Task GetProducts_PublicEndpoint_ReturnsOkWithPaginatedJsonStructure()
    {
        // Arrange: Sembrar 2 productos activos en SQLite
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            db.Set<Product>().AddRange(
                new Product("SKU-PUB-01", "Auriculares Bluetooth", 50000m, 20, "Noise Cancelling", "INT-PUB-01"),
                new Product("SKU-PUB-02", "Webcam Full HD", 40000m, 12, "1080p", "INT-PUB-02")
            );
            await db.SaveChangesAsync();
        }

        ClearAuthentication(); // Endpoint público [AllowAnonymous]

        // Act
        var response = await _client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagination = await response.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>(_jsonOptions);

        pagination.Should().NotBeNull();
        pagination!.Total.Should().Be(2);
        pagination.ProductItems.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetProducts_WithQueryStringFilters_ReturnsFilteredResultsFromSqlite()
    {
        // Arrange: Sembrar 3 productos en SQLite
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            db.Set<Product>().AddRange(
                new Product("SKU-FLT-01", "Mouse Gamer Logitech", 35000m, 10, "RGB", "INT-FLT-01"),
                new Product("SKU-FLT-02", "Teclado Redragon", 42000m, 10, "Mecánico", "INT-FLT-02"),
                new Product("SKU-FLT-03", "Monitor Samsung", 210000m, 5, "24 pulgadas", "INT-FLT-03")
            );
            await db.SaveChangesAsync();
        }

        ClearAuthentication();

        // Act: Filtrar por search=Mouse con paginación
        var response = await _client.GetAsync("/api/products?search=Mouse&pageNumber=1&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagination = await response.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>(_jsonOptions);

        pagination.Should().NotBeNull();
        pagination!.Total.Should().Be(1);
        pagination.ProductItems.Should().ContainSingle();
        pagination.ProductItems[0].Name.Should().Contain("Mouse");
    }

    [Fact]
    public async Task GetAuthProducts_AdminEndpoint_WhenCalledByAdmin_IncludesInactiveProducts()
    {
        // Arrange: Sembrar 1 producto activo y 1 producto inactivo (IsActive = false)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            var activeProduct = new Product("SKU-ADM-01", "Silla Ergonómica", 180000m, 4, "Activa", "INT-ADM-01");
            var inactiveProduct = new Product("SKU-ADM-02", "Escritorio Discontinuado", 95000m, 0, "Inactivo", "INT-ADM-02")
            {
                IsActive = false
            };

            db.Set<Product>().AddRange(activeProduct, inactiveProduct);
            await db.SaveChangesAsync();
        }

        AuthenticateAs("admin", "Admin");

        // Act
        var response = await _client.GetAsync("/api/products/admin");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagination = await response.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>(_jsonOptions);

        pagination.Should().NotBeNull();
        pagination!.Total.Should().Be(2);
        pagination.ProductItems.Should().Contain(p => p.Sku == "SKU-ADM-02" && !p.IsActive);
    }
}