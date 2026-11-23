using System.Linq.Expressions;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Dsw2025Tpi.UnitTests.Services;

public class ProductsManagementServiceTests
{
    private readonly Mock<IRepository> _repositoryMock;
    private readonly Mock<ILogger<ProductsManagementService>> _loggerMock;
    private readonly ProductsManagementService _service;

    public ProductsManagementServiceTests()
    {
        _repositoryMock = new Mock<IRepository>();
        _loggerMock = new Mock<ILogger<ProductsManagementService>>();
        _service = new ProductsManagementService(_repositoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task AddProduct_DatosValidos_CreaProductoYLLamaARepositoryAdd()
    {
        // Arrange
        var request = new ProductModel.Request(
            "SKU-001", "INT-001", "Mouse Gamer", "Mouse inalámbrico RGB", 25000m, 15);

        _repositoryMock
            .Setup(r => r.First<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()))
            .ReturnsAsync((Product?)null);

        _repositoryMock
            .Setup(r => r.Add(It.IsAny<Product>()))
            .ReturnsAsync((Product p) => p);

        // Act
        var result = await _service.AddProduct(request);

        // Assert
        result.Should().NotBeNull();
        result.Sku.Should().Be("SKU-001");
        result.Name.Should().Be("Mouse Gamer");
        result.CurrentUnitPrice.Should().Be(25000m);
        result.StockQuantity.Should().Be(15);

        _repositoryMock.Verify(
            r => r.Add(It.Is<Product>(p =>
                p.Sku == "SKU-001" &&
                p.Name == "Mouse Gamer" &&
                p.CurrentUnitPrice == 25000m &&
                p.StockQuantity == 15)),
            Times.Once);
    }

    [Theory]
    [InlineData("", "Mouse Gamer")]
    [InlineData("   ", "Mouse Gamer")]
    [InlineData("SKU-001", "")]
    [InlineData("SKU-001", "   ")]
    public async Task AddProduct_SkuONombreVacio_LanzaInvalidDataException(string sku, string name)
    {
        // Arrange
        var request = new ProductModel.Request(sku, "INT-001", name, "Descripción", 1000m, 10);

        // Act
        Func<Task> act = () => _service.AddProduct(request);

        // Assert
        await act.Should().ThrowAsync<InvalidDataException>();
        _repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task AddProduct_PrecioNegativo_LanzaInvalidDataException()
    {
        // Arrange
        var request = new ProductModel.Request("SKU-001", "INT-001", "Mouse", "Desc", -100m, 10);

        // Act
        Func<Task> act = () => _service.AddProduct(request);

        // Assert
        await act.Should().ThrowAsync<InvalidDataException>();
        _repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task AddProduct_StockMenorOIgualACero_LanzaInvalidDataException(int invalidStock)
    {
        // Arrange
        var request = new ProductModel.Request("SKU-001", "INT-001", "Mouse", "Desc", 1000m, invalidStock);

        // Act
        Func<Task> act = () => _service.AddProduct(request);

        // Assert
        await act.Should().ThrowAsync<InvalidDataException>();
        _repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task AddProduct_SkuDuplicado_LanzaDuplicatedEntityExceptionYNoGuarda()
    {
        // Arrange
        var existingProduct = new Product("SKU-001", "Existente", 1000m, 5, "Desc", "INT-001");
        var request = new ProductModel.Request("SKU-001", "INT-002", "Nuevo", "Desc", 2000m, 10);

        _repositoryMock
            .Setup(r => r.First<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()))
            .ReturnsAsync(existingProduct);

        // Act
        Func<Task> act = () => _service.AddProduct(request);

        // Assert
        await act.Should().ThrowAsync<DuplicatedEntityException>();
        _repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task GetProductById_CuandoExiste_DevuelveProducto()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product("SKU-001", "Teclado Mecánico", 45000m, 8, "Switch Red", "INT-001")
        {
            IsActive = true
        };

        _repositoryMock
            .Setup(r => r.GetById<Product>(productId, It.IsAny<string[]>()))
            .ReturnsAsync(product);

        // Act
        var result = await _service.GetProductById(productId);

        // Assert
        result.Should().NotBeNull();
        result!.Sku.Should().Be("SKU-001");
        result.Name.Should().Be("Teclado Mecánico");
        result.CurrentUnitPrice.Should().Be(45000m);
    }

    [Fact]
    public async Task GetProductById_CuandoNoExiste_LanzaKeyNotFoundException()
    {
        // Arrange
        var productId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetById<Product>(productId, It.IsAny<string[]>()))
            .ReturnsAsync((Product?)null);

        // Act
        Func<Task> act = () => _service.GetProductById(productId);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetProducts_SinIncluirInactivos_DevuelveSoloActivosYRespetaPaginacion()
    {
        // Arrange
        var products = new List<Product>
        {
            new("SKU-01", "Auriculares", 20000m, 5, "Desc", "INT-01") { IsActive = true },
            new("SKU-02", "Monitor", 150000m, 3, "Desc", "INT-02") { IsActive = true },
            new("SKU-03", "Mouse", 15000m, 10, "Desc", "INT-03") { IsActive = true },
            new("SKU-04", "Producto Inactivo", 5000m, 2, "Desc", "INT-04") { IsActive = false }
        };

        _repositoryMock
            .Setup(r => r.GetFiltered<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()))
            .ReturnsAsync((Expression<Func<Product, bool>> predicate, string[] _) =>
                products.Where(predicate.Compile()).ToList());

        var filter = new ProductModel.FilterProduct(Status: null, Search: null, PageNumber: 1, PageSize: 2);

        // Act
        var result = await _service.GetProducts(filter, includeInactive: false);

        // Assert
        result.Total.Should().Be(3);
        result.ProductItems.Should().HaveCount(2);
        result.ProductItems.Should().OnlyContain(p => p.IsActive);
        result.ProductItems[0].Name.Should().Be("Auriculares");
        result.ProductItems[1].Name.Should().Be("Monitor");
    }

    [Fact]
    public async Task GetProducts_ConTerminoDeBusqueda_FiltraPorNombreSkuOCodigoInterno()
    {
        // Arrange
        var products = new List<Product>
        {
            new("SKU-MOUSE", "Mouse Gamer", 15000m, 10, "Desc", "CODE-M") { IsActive = true },
            new("SKU-TECLADO", "Teclado", 30000m, 5, "Desc", "CODE-T") { IsActive = true }
        };

        _repositoryMock
            .Setup(r => r.GetFiltered<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()))
            .ReturnsAsync((Expression<Func<Product, bool>> predicate, string[] _) =>
                products.Where(predicate.Compile()).ToList());

        var filter = new ProductModel.FilterProduct(Status: null, Search: "Mouse", PageNumber: 1, PageSize: 10);

        // Act
        var result = await _service.GetProducts(filter);

        // Assert
        result.Total.Should().Be(1);
        result.ProductItems.Should().ContainSingle(p => p.Sku == "SKU-MOUSE");
    }

    [Fact]
    public async Task UpdateProduct_DatosValidos_ActualizaYDevuelveProducto()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var existing = new Product("SKU-OLD", "Nombre Viejo", 1000m, 5, "Desc vieja", "INT-OLD")
        {
            IsActive = true
        };
        var request = new ProductModel.Request("SKU-NEW", "INT-NEW", "Nombre Nuevo", "Desc nueva", 2500m, 20);

        _repositoryMock
            .Setup(r => r.GetById<Product>(productId, It.IsAny<string[]>()))
            .ReturnsAsync(existing);

        _repositoryMock
            .Setup(r => r.First<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()))
            .ReturnsAsync((Product?)null);

        _repositoryMock
            .Setup(r => r.Update(It.IsAny<Product>()))
            .ReturnsAsync((Product p) => p);

        // Act
        var result = await _service.UpdateProduct(productId, request);

        // Assert
        result.Sku.Should().Be("SKU-NEW");
        result.Name.Should().Be("Nombre Nuevo");
        result.CurrentUnitPrice.Should().Be(2500m);
        result.StockQuantity.Should().Be(20);
        _repositoryMock.Verify(r => r.Update(existing), Times.Once);
    }

    [Fact]
    public async Task DisableProduct_CuandoExiste_CambiaIsActiveAFalseYActualiza()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var existing = new Product("SKU-001", "Mouse", 1000m, 5, "Desc", "INT-001")
        {
            IsActive = true
        };

        _repositoryMock
            .Setup(r => r.GetById<Product>(productId, It.IsAny<string[]>()))
            .ReturnsAsync(existing);

        _repositoryMock
            .Setup(r => r.Update(It.IsAny<Product>()))
            .ReturnsAsync((Product p) => p);

        // Act
        await _service.DisableProduct(productId);

        // Assert
        existing.IsActive.Should().BeFalse();
        _repositoryMock.Verify(r => r.Update(It.Is<Product>(p => !p.IsActive)), Times.Once);
    }
}