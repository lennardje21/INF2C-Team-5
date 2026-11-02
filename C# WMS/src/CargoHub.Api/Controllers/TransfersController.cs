using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

// BaseController for stock transfers.
[ApiController]
[Route("api/v1/transfers")]
public class TransfersController(TransfersService transfersService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Transfer>>> GetTransfers(
        CancellationToken cancellationToken)
    {
        return Ok(await transfersService.GetAllAsync(cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Transfer>> GetTransfer(
        int id, CancellationToken cancellationToken)
    {
        var transfer = await transfersService.GetByIdAsync(id, cancellationToken);
        return transfer is null ? NotFound() : Ok(transfer);
    }

    [HttpGet("{id:int}/items")]
    public async Task<ActionResult<IEnumerable<TransferItem>>> GetTransferItems(
        int id, CancellationToken cancellationToken)
    {
        var items = await transfersService.GetItemsAsync(id, cancellationToken);
        return items is null ? NotFound() : Ok(items);
    }

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
            return BadRequest(e.Message);
        }
    }

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
            return BadRequest(e.Message);
        }
    }

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
            return BadRequest(e.Message);
        }
    }

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
            return BadRequest(e.Message);
        }
    }
}