using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

/// HTTP endpoints for item groups; the logic is in ItemGroupsService.
[ApiController]
[Route("api/v1/item-groups")]
public class ItemGroupsController(ItemGroupsService itemGroupsService) : ControllerBase
{
    /// Returns all item groups.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ItemGroup>>> GetItemGroups(CancellationToken cancellationToken)
    {
        return Ok(await itemGroupsService.GetAllAsync(cancellationToken));
    }

    /// Returns one item group.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ItemGroup>> GetItemGroup(int id, CancellationToken cancellationToken)
    {
        var itemGroup = await itemGroupsService.GetByIdAsync(id, cancellationToken);
        return itemGroup is null ? NotFound() : Ok(itemGroup);
    }

    /// Creates an item group.
    [HttpPost]
    public async Task<ActionResult<ItemGroup>> CreateItemGroup(
        ItemGroup itemGroup, CancellationToken cancellationToken)
    {
        try
        {
            var created = await itemGroupsService.CreateAsync(itemGroup, cancellationToken);
            return CreatedAtAction(nameof(GetItemGroup), new { id = created.Id }, created);
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// Updates an item group.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateItemGroup(
        int id, ItemGroup itemGroup, CancellationToken cancellationToken)
    {
        try
        {
            return await itemGroupsService.UpdateAsync(id, itemGroup, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// Deletes an item group that is not referenced by items.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteItemGroup(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await itemGroupsService.DeleteAsync(id, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }
}