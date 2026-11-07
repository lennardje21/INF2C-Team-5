using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

/// <summary>
/// HTTP endpoints for inventories; the logic is in InventoriesService.
/// An inventory is identified by item id + location id, so both are in the URL.
/// </summary>
[ApiController]
[Route("api/v1/inventories")]
public class InventoriesController(InventoriesService inventoriesService) : ControllerBase
{
    /// <summary>Returns all inventories.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Inventory>>> GetInventories(CancellationToken cancellationToken)
    {
        return Ok(await inventoriesService.GetAllAsync(cancellationToken));
    }

    /// <summary>Returns the inventory of one item at one location.</summary>
    [HttpGet("{itemId:int}/{locationId:int}")]
    public async Task<ActionResult<Inventory>> GetInventory(int itemId, int locationId,
        CancellationToken cancellationToken)
    {
        var inventory = await inventoriesService.GetByKeyAsync(itemId, locationId, cancellationToken);

        if (inventory is null)
        {
            return NotFound();
        }

        return Ok(inventory);
    }

    /// <summary>Creates an inventory; returns 409 if it already exists.</summary>
    [HttpPost]
    public async Task<ActionResult<Inventory>> CreateInventory(Inventory inventory,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await inventoriesService.CreateAsync(inventory, cancellationToken);

            return CreatedAtAction(nameof(GetInventory),
                new { itemId = created.ItemId, locationId = created.LocationId }, created);
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
        catch (InvalidOperationException e)
        {
            return Conflict(e.Message);
        }
    }

    /// <summary>Updates the quantities of an inventory.</summary>
    [HttpPut("{itemId:int}/{locationId:int}")]
    public async Task<IActionResult> UpdateInventory(int itemId, int locationId, Inventory inventory,
        CancellationToken cancellationToken)
    {
        try
        {
            return await inventoriesService.UpdateAsync(itemId, locationId, inventory, cancellationToken)
                ? NoContent() : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>Deletes an inventory.</summary>
    [HttpDelete("{itemId:int}/{locationId:int}")]
    public async Task<IActionResult> DeleteInventory(int itemId, int locationId,
        CancellationToken cancellationToken)
    {
        return await inventoriesService.DeleteAsync(itemId, locationId, cancellationToken)
            ? NoContent() : NotFound();
    }
}