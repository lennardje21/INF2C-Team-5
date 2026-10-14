using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

/// <summary>HTTP endpoints for shipments; the logic is in ShipmentsService.</summary>
[ApiController]
[Route("api/v1/shipments")]
public class ShipmentsController(ShipmentsService shipmentsService) : ControllerBase
{
    /// <summary>Returns all shipments with their items.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Shipment>>> GetShipments(CancellationToken cancellationToken)
    {
        return Ok(await shipmentsService.GetAllAsync(cancellationToken));
    }

    /// <summary>Returns one shipment with its items.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Shipment>> GetShipment(int id, CancellationToken cancellationToken)
    {
        var shipment = await shipmentsService.GetByIdAsync(id, cancellationToken);

        if (shipment is null)
        {
            return NotFound();
        }

        return Ok(shipment);
    }

    /// <summary>Returns the order id of the shipment, as a list.</summary>
    [HttpGet("{id:int}/orders")]
    public async Task<ActionResult<IEnumerable<int>>> GetShipmentOrders(int id, CancellationToken cancellationToken)
    {
        var shipment = await shipmentsService.GetByIdAsync(id, cancellationToken);

        if (shipment is null)
        {
            return NotFound();
        }

        return Ok(new[] { shipment.OrderId });
    }

    /// <summary>Returns the items of the shipment.</summary>
    [HttpGet("{id:int}/items")]
    public async Task<ActionResult<IEnumerable<ShipmentItem>>> GetShipmentItems(int id, CancellationToken cancellationToken)
    {
        var items = await shipmentsService.GetItemsAsync(id, cancellationToken);

        if (items is null)
        {
            return NotFound();
        }

        return Ok(items);
    }

    /// <summary>Creates a shipment with its items.</summary>
    [HttpPost]
    public async Task<ActionResult<Shipment>> CreateShipment(Shipment shipment, CancellationToken cancellationToken)
    {
        try
        {
            var created = await shipmentsService.CreateAsync(shipment, cancellationToken);

            return CreatedAtAction(nameof(GetShipment), new { id = created.Id }, created);
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>Updates the shipment's fields, not its items.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateShipment(int id, Shipment shipment, CancellationToken cancellationToken)
    {
        try
        {
            return await shipmentsService.UpdateAsync(id, shipment, cancellationToken) ? NoContent() : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>Links the shipment to another order.</summary>
    [HttpPut("{id:int}/orders")]
    public async Task<IActionResult> UpdateShipmentOrders(int id, List<int> orderIds, CancellationToken cancellationToken)
    {
        try
        {
            return await shipmentsService.UpdateOrdersAsync(id, orderIds, cancellationToken) ? NoContent() : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>Replaces the items of the shipment.</summary>
    [HttpPut("{id:int}/items")]
    public async Task<IActionResult> UpdateShipmentItems(int id, List<ShipmentItem> items, CancellationToken cancellationToken)
    {
        try
        {
            return await shipmentsService.UpdateItemsAsync(id, items, cancellationToken) ? NoContent() : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>Deletes the shipment and its items.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteShipment(int id, CancellationToken cancellationToken)
    {
        return await shipmentsService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}
