using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

[ApiController]
[Route("api/v1/shipments")]
public class ShipmentsController(ShipmentsService shipmentsService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Shipment>>> GetShipments(CancellationToken cancellationToken)
    {
        return Ok(await shipmentsService.GetAllAsync(cancellationToken));
    }

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

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteShipment(int id, CancellationToken cancellationToken)
    {
        return await shipmentsService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}
