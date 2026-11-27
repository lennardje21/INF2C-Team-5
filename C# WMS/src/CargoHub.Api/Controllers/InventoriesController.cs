using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using CargoHub.Api.Validation;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;


// HTTP endpoints for inventories

[ApiController]
[Route("api/v2/inventories")]
public class InventoriesController(InventoriesService inventoriesService) : ControllerBase
{
    // Returns one page of inventories.
    [HttpGet]
    [ProducesResponseType<PagedResult<Inventory>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<Inventory>>> GetInventories(
        [FromQuery] ListQuery query, CancellationToken cancellationToken)
    {
        return Ok(await inventoriesService.GetPageAsync(query.ToPageRequest(), cancellationToken));
    }

    // Returns the inventory of one item at one location.
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

    // Creates an inventory; returns 409 if it already exists.
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
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
        catch (InvalidOperationException e)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: e.Message);
        }
    }

    // Updates the quantities of an inventory.
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
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }

    // Deletes an inventory.
    [HttpDelete("{itemId:int}/{locationId:int}")]
    public async Task<IActionResult> DeleteInventory(int itemId, int locationId,
        CancellationToken cancellationToken)
    {
        return await inventoriesService.DeleteAsync(itemId, locationId, cancellationToken)
            ? NoContent() : NotFound();
    }
}