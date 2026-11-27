using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using CargoHub.Api.Validation;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

/// HTTP endpoints for item lines; the logic is in ItemLinesService.
[ApiController]
[Route("api/v2/item_lines")]
public class ItemLinesController(ItemLinesService itemLinesService) : ControllerBase
{
    /// Returns one page of item lines.
    [HttpGet]
    [ProducesResponseType<PagedResult<ItemLine>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ItemLine>>> GetItemLines(
        [FromQuery] ListQuery query, CancellationToken cancellationToken)
    {
        return Ok(await itemLinesService.GetPageAsync(query.ToPageRequest(), cancellationToken));
    }

    /// Returns one item line.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ItemLine>> GetItemLine(int id, CancellationToken cancellationToken)
    {
        var itemLine = await itemLinesService.GetByIdAsync(id, cancellationToken);
        return itemLine is null ? NotFound() : Ok(itemLine);
    }

    /// Returns the IDs of items in an item line.
    [HttpGet("{id:int}/items")]
    public async Task<ActionResult<IEnumerable<int>>> GetItemLineItems(
        int id, CancellationToken cancellationToken)
    {
        var items = await itemLinesService.GetItemsAsync(id, cancellationToken);
        return Ok(items);
    }

    /// Creates an item line.
    [HttpPost]
    public async Task<ActionResult<ItemLine>> CreateItemLine(
        ItemLine itemLine, CancellationToken cancellationToken)
    {
        try
        {
            var created = await itemLinesService.CreateAsync(itemLine, cancellationToken);
            return CreatedAtAction(nameof(GetItemLine), new { id = created.Id }, created);
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }

    /// Updates an item line.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateItemLine(
        int id, ItemLine itemLine, CancellationToken cancellationToken)
    {
        try
        {
            return await itemLinesService.UpdateAsync(id, itemLine, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }

    /// Deletes an item line that is not referenced by items.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteItemLine(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await itemLinesService.DeleteAsync(id, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }
}