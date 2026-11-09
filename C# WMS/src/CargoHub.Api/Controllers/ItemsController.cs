using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

// HTTP endpoints for items; the logic is in ItemsService.
[ApiController]
[Route("api/v1/items")]
public class ItemsController(ItemsService itemsService) : ControllerBase
{
    // Returns all items.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Item>>> GetItems(CancellationToken cancellationToken)
    {
        return Ok(await itemsService.GetAllAsync(cancellationToken));
    }

    // Returns one item.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Item>> GetItem(int id, CancellationToken cancellationToken)
    {
        var item = await itemsService.GetByIdAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    // Returns the inventory for an item at each location.
    [HttpGet("{id:int}/inventory")]
    public async Task<ActionResult<IEnumerable<Inventory>>> GetItemInventory(
        int id, CancellationToken cancellationToken)
    {
        var inventory = await itemsService.GetInventoryAsync(id, cancellationToken);
        return inventory is null ? NotFound() : Ok(inventory);
    }

    // Returns the combined inventory quantities for an item.
    [HttpGet("{id:int}/inventory/totals")]
    public async Task<ActionResult<ItemInventoryTotals>> GetItemInventoryTotals(
        int id, CancellationToken cancellationToken)
    {
        var totals = await itemsService.GetInventoryTotalsAsync(id, cancellationToken);
        return totals is null ? NotFound() : Ok(totals);
    }

    // Creates an item.
    [HttpPost]
    public async Task<ActionResult<Item>> CreateItem(Item item, CancellationToken cancellationToken)
    {
        try
        {
            var created = await itemsService.CreateAsync(item, cancellationToken);
            return CreatedAtAction(nameof(GetItem), new { id = created.Id }, created);
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    // Updates an item's fields.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateItem(
        int id, Item item, CancellationToken cancellationToken)
    {
        try
        {
            return await itemsService.UpdateAsync(id, item, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    // Deletes an item that is not referenced by other records.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteItem(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await itemsService.DeleteAsync(id, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }
}
