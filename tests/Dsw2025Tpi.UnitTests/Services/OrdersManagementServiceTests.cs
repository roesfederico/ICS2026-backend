using System.Linq.Expressions;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Enums;
using Dsw2025Tpi.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Dsw2025Tpi.UnitTests.Services;

public class OrdersManagementServiceTests
{
    private readonly Mock<IRepository> _repositoryMock;
    private readonly Mock<ILogger<OrdersManagementService>> _loggerMock;
    private readonly OrdersManagementService _service;

    public OrdersManagementServiceTests()
    {
        _repositoryMock = new Mock<IRepository>();
        _loggerMock = new Mock<ILogger<OrdersManagementService>>();
        _service = new OrdersManagementService(_repositoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CreateOrder_DatosValidosYStockSuficiente_CalculaTotalYDescuentaStock()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var customer = new Customer { Name = "Juan Pérez", Email = "juan@test.com", PhoneNumber = "3815551234" };
        var product = new Product("SKU-001", "Mouse Gamer", 10000m, 10, "Desc", "INT-001") { IsActive = true };

        var request = new OrderRequest(
            customerId,
            "Av. Sarmiento 500",
            "Av. Sarmiento 500",
            "Entregar por la tarde",
            new List<OrderItemRequest> { new(productId, 3) });

        _repositoryMock
            .Setup(r => r.GetById<Customer>(customerId, It.IsAny<string[]>()))
            .ReturnsAsync(customer);

        _repositoryMock
            .Setup(r => r.GetById<Product>(productId, It.IsAny<string[]>()))
            .ReturnsAsync(product);

        _repositoryMock
            .Setup(r => r.Update(It.IsAny<Product>()))
            .ReturnsAsync((Product p) => p);

        _repositoryMock
            .Setup(r => r.Add(It.IsAny<Order>()))
            .ReturnsAsync((Order o) => o);

        // Act
        var result = await _service.CreateOrder(request);

        // Assert
        result.Should().NotBeNull();
        result.TotalAmount.Should().Be(30000m);
        result.ClientName.Should().Be("Juan Pérez");
        result.Items.Should().ContainSingle();
        product.StockQuantity.Should().Be(7);

        _repositoryMock.Verify(r => r.Update(It.Is<Product>(p => p.StockQuantity == 7)), Times.Once);
        _repositoryMock.Verify(r => r.Add(It.Is<Order>(o => o.TotalAmount == 30000m)), Times.Once);
    }

    [Fact]
    public async Task CreateOrder_SinItems_LanzaInvalidOrderDataException()
    {
        // Arrange
        var request = new OrderRequest(
            Guid.NewGuid(), "Dirección 123", "Dirección 123", null, new List<OrderItemRequest>());

        // Act
        Func<Task> act = () => _service.CreateOrder(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOrderDataException>();
        _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrder_ClienteInexistente_LanzaCustomerNotFoundException()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var request = new OrderRequest(
            customerId, "Dirección 123", "Dirección 123", null,
            new List<OrderItemRequest> { new(Guid.NewGuid(), 1) });

        _repositoryMock
            .Setup(r => r.GetById<Customer>(customerId, It.IsAny<string[]>()))
            .ReturnsAsync((Customer?)null);

        // Act
        Func<Task> act = () => _service.CreateOrder(request);

        // Assert
        await act.Should().ThrowAsync<CustomerNotFoundException>();
        _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task CreateOrder_CantidadInvalida_LanzaInvalidOrderDataException(int quantity)
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new Customer { Name = "Juan Pérez", Email = "juan@test.com" };
        var request = new OrderRequest(
            customerId, "Dirección 123", "Dirección 123", null,
            new List<OrderItemRequest> { new(Guid.NewGuid(), quantity) });

        _repositoryMock
            .Setup(r => r.GetById<Customer>(customerId, It.IsAny<string[]>()))
            .ReturnsAsync(customer);

        // Act
        Func<Task> act = () => _service.CreateOrder(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOrderDataException>();
        _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrder_ProductoInactivo_LanzaArgumentException()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var customer = new Customer { Name = "Juan Pérez", Email = "juan@test.com" };
        var inactiveProduct = new Product("SKU-001", "Mouse", 1000m, 10, "Desc", "INT-001") { IsActive = false };

        var request = new OrderRequest(
            customerId, "Dirección 123", "Dirección 123", null,
            new List<OrderItemRequest> { new(productId, 1) });

        _repositoryMock
            .Setup(r => r.GetById<Customer>(customerId, It.IsAny<string[]>()))
            .ReturnsAsync(customer);

        _repositoryMock
            .Setup(r => r.GetById<Product>(productId, It.IsAny<string[]>()))
            .ReturnsAsync(inactiveProduct);

        // Act
        Func<Task> act = () => _service.CreateOrder(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrder_StockInsuficiente_LanzaInsufficientStockExceptionYNoGuardaOrden()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var customer = new Customer { Name = "Juan Pérez", Email = "juan@test.com" };
        var product = new Product("SKU-001", "Mouse Gamer", 10000m, 2, "Desc", "INT-001") { IsActive = true };

        var request = new OrderRequest(
            customerId, "Dirección 123", "Dirección 123", null,
            new List<OrderItemRequest> { new(productId, 5) });

        _repositoryMock
            .Setup(r => r.GetById<Customer>(customerId, It.IsAny<string[]>()))
            .ReturnsAsync(customer);

        _repositoryMock
            .Setup(r => r.GetById<Product>(productId, It.IsAny<string[]>()))
            .ReturnsAsync(product);

        // Act
        Func<Task> act = () => _service.CreateOrder(request);

        // Assert
        await act.Should().ThrowAsync<InsufficientStockException>();
        product.StockQuantity.Should().Be(2);
        _repositoryMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
        _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task GetOrderById_CuandoNoExiste_LanzaOrderNotFoundException()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetById<Order>(orderId, It.IsAny<string[]>()))
            .ReturnsAsync((Order?)null);

        // Act
        Func<Task> act = () => _service.GetOrderById(orderId);

        // Assert
        await act.Should().ThrowAsync<OrderNotFoundException>();
    }

    [Fact]
    public async Task GetOrders_ConFiltroDeBusqueda_FiltraPorNombreDeCliente()
    {
        // Arrange
        var orderJuan = new Order(Guid.NewGuid(), "Dir 1", "Dir 1", null)
        {
            TotalAmount = 1000m,
            Customer = new Customer { Name = "Juan Pérez", Email = "juan@test.com" },
            OrderItems = new List<OrderItem>()
        };
        var orderAna = new Order(Guid.NewGuid(), "Dir 2", "Dir 2", null)
        {
            TotalAmount = 2000m,
            Customer = new Customer { Name = "Ana Gómez", Email = "ana@test.com" },
            OrderItems = new List<OrderItem>()
        };

        _repositoryMock
            .Setup(r => r.GetFiltered<Order>(
                It.IsAny<Expression<Func<Order, bool>>>(),
                It.IsAny<string[]>()))
            .ReturnsAsync(new List<Order> { orderJuan, orderAna });

        var filter = new OrderFilter(Search: "ana", Status: null, PageNumber: 1, PageSize: 10);

        // Act
        var result = await _service.GetOrders(filter);

        // Assert
        result.Total.Should().Be(1);
        result.Items.Should().ContainSingle(o => o.ClientName == "Ana Gómez");
    }

    [Fact]
    public async Task UpdateOrderStatus_EstadoValido_ActualizaEstadoYGuarda()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var targetStatus = Enum.GetValues<OrderStatus>().Last();
        var order = new Order(Guid.NewGuid(), "Dir 1", "Dir 1", null)
        {
            OrderItems = new List<OrderItem>(),
            Customer = new Customer { Name = "Juan Pérez", Email = "juan@test.com" }
        };

        _repositoryMock
            .Setup(r => r.GetById<Order>(orderId, It.IsAny<string[]>()))
            .ReturnsAsync(order);

        _repositoryMock
            .Setup(r => r.Update(It.IsAny<Order>()))
            .ReturnsAsync((Order o) => o);

        // Act
        var result = await _service.UpdateOrderStatus(orderId, targetStatus.ToString());

        // Assert
        result.Status.Should().Be(targetStatus);
        order.Status.Should().Be(targetStatus);
        _repositoryMock.Verify(r => r.Update(order), Times.Once);
    }

    [Fact]
    public async Task UpdateOrderStatus_EstadoInvalido_LanzaInvalidOrderStatusException()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var order = new Order(Guid.NewGuid(), "Dir 1", "Dir 1", null);

        _repositoryMock
            .Setup(r => r.GetById<Order>(orderId, It.IsAny<string[]>()))
            .ReturnsAsync(order);

        // Act
        Func<Task> act = () => _service.UpdateOrderStatus(orderId, "ESTADO_INEXISTENTE");

        // Assert
        await act.Should().ThrowAsync<InvalidOrderStatusException>();
        _repositoryMock.Verify(r => r.Update(It.IsAny<Order>()), Times.Never);
    }
}