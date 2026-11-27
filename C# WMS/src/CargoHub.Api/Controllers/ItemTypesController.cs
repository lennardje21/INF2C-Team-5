using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using CargoHub.Api.Validation;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

/// HTTP endpoints for item types; the logic is in ItemTypesService.
[ApiController]
[Route("api/v2/item_types")]
public class ItemTypesController(ItemTypesService itemTypesService) : ControllerBase
{
    /// Returns one page of item types.
    [HttpGet]
    [ProducesResponseType<PagedResult<ItemType>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ItemType>>> GetItemTypes(
        [FromQuery] ListQuery query, CancellationToken cancellationToken)
    {
        return Ok(await itemTypesService.GetPageAsync(query.ToPageRequest(), cancellationToken));
    }

    /// Returns one item type.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ItemType>> GetItemType(int id, CancellationToken cancellationToken)
    {
        var itemType = await itemTypesService.GetByIdAsync(id, cancellationToken);
        return itemType is null ? NotFound() : Ok(itemType);
    }

    /// Returns the IDs of items of an item type.
    [HttpGet("{id:int}/items")]
    public async Task<ActionResult<IEnumerable<int>>> GetItemTypeItems(
        int id, CancellationToken cancellationToken)
    {
        var items = await itemTypesService.GetItemsAsync(id, cancellationToken);
        return Ok(items);
    }

    /// Creates an item type.
    [HttpPost]
    public async Task<ActionResult<ItemType>> CreateItemType(
        ItemType itemType, CancellationToken cancellationToken)
    {
        try
        {
            var created = await itemTypesService.CreateAsync(itemType, cancellationToken);
            return CreatedAtAction(nameof(GetItemType), new { id = created.Id }, created);
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }

    /// Updates an item type.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateItemType(
        int id, ItemType itemType, CancellationToken cancellationToken)
    {
        try
        {
            return await itemTypesService.UpdateAsync(id, itemType, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }

    /// Deletes an item type that is not referenced by items.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteItemType(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await itemTypesService.DeleteAsync(id, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }
}
