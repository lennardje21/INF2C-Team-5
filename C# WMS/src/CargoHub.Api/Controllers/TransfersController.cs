using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

// BaseController for stock transfers.
[ApiController]
[Route("api/v2/transfers")]
public class TransfersController(TransfersService transfersService) : ControllerBase
{
    // Get all transfers, including their items.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Transfer>>> GetTransfers(
        CancellationToken cancellationToken)
    {
        return Ok(await transfersService.GetAllAsync(cancellationToken));
    }

    // Get a single transfer by ID, including its items. Returns 404 if not found.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Transfer>> GetTransfer(
        int id, CancellationToken cancellationToken)
    {
        var transfer = await transfersService.GetByIdAsync(id, cancellationToken);
        return transfer is null ? NotFound() : Ok(transfer);
    }

    // Get the items of a transfer by transfer ID. Returns 404 if the transfer is not found.
    [HttpGet("{id:int}/items")]
    public async Task<ActionResult<IEnumerable<TransferItem>>> GetTransferItems(
        int id, CancellationToken cancellationToken)
    {
        var items = await transfersService.GetItemsAsync(id, cancellationToken);
        return items is null ? NotFound() : Ok(items);
    }

    // Create a new transfer, including its items. Returns 400 if validation fails.
    [HttpPost]
    public async Task<ActionResult<Transfer>> CreateTransfer(
        Transfer transfer, CancellationToken cancellationToken)
    {
        try
        {
            var created = await transfersService.CreateAsync(transfer, cancellationToken);
            return CreatedAtAction(nameof(GetTransfer), new { id = created.Id }, created);
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }

    // Update an existing transfer, including its items. Returns 404 if the transfer is not found, or 400 if validation fails.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateTransfer(
        int id, Transfer transfer, CancellationToken cancellationToken)
    {
        try
        {
            return await transfersService.UpdateAsync(id, transfer, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }

    // Commit a transfer, moving the items from the source location to the destination location. The transfer status is updated to "Processed". Returns 404 if the transfer is not found, or 400 if validation fails.
    [HttpPut("{id:int}/commit")]
    public async Task<IActionResult> CommitTransfer(
        int id, CancellationToken cancellationToken)
    {
        try
        {
            return await transfersService.CommitAsync(id, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }

    // Delete a transfer, including its items. The transfer must not have been processed yet. Returns 404 if the transfer is not found, or 400 if the transfer has already been processed.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteTransfer(
        int id, CancellationToken cancellationToken)
    {
        try
        {
            return await transfersService.DeleteAsync(id, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }
}