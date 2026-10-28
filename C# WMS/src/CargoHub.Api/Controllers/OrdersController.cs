using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

/// <summary>HTTP endpoints for orders; the logic is in OrdersService.</summary>
[ApiController]
[Route("api/v1/orders")]
public class OrdersController(OrdersService ordersService) : ControllerBase
{
    /// <summary>Returns all orders with their items.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Order>>> GetOrders(CancellationToken cancellationToken)
    {
        return Ok(await ordersService.GetAllAsync(cancellationToken));
    }

    /// <summary>Returns one order with its items.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Order>> GetOrder(int id, CancellationToken cancellationToken)
    {
        var order = await ordersService.GetByIdAsync(id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        return Ok(order);
    }

    /// <summary>Returns the items of the order.</summary>
    [HttpGet("{id:int}/items")]
    public async Task<ActionResult<IEnumerable<OrderItem>>> GetOrderItems(int id, CancellationToken cancellationToken)
    {
        var items = await ordersService.GetItemsAsync(id, cancellationToken);

        if (items is null)
        {
            return NotFound();
        }

        return Ok(items);
    }

    /// <summary>Creates an order with its items.</summary>
    [HttpPost]
    public async Task<ActionResult<Order>> CreateOrder(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var created = await ordersService.CreateAsync(order, cancellationToken);

            return CreatedAtAction(nameof(GetOrder), new { id = created.Id }, created);
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>Updates the order's fields, not its items.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateOrder(int id, Order order, CancellationToken cancellationToken)
    {
        try
        {
            return await ordersService.UpdateAsync(id, order, cancellationToken) ? NoContent() : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>Replaces the items of the order.</summary>
    [HttpPut("{id:int}/items")]
    public async Task<IActionResult> UpdateOrderItems(int id, List<OrderItem> items, CancellationToken cancellationToken)
    {
        try
        {
            return await ordersService.UpdateItemsAsync(id, items, cancellationToken) ? NoContent() : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>Deletes the order and its items; returns 409 if the order still has shipments.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteOrder(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await ordersService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException e)
        {
            return Conflict(e.Message);
        }
    }
}
