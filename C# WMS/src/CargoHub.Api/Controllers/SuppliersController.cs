using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using CargoHub.Api.Validation;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

/// <summary>HTTP endpoints for suppliers; the logic is in SuppliersService.</summary>
[ApiController]
[Route("api/v2/suppliers")]
public class SuppliersController(SuppliersService suppliersService) : ControllerBase
{
    /// <summary>Returns one page of suppliers.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<Supplier>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<Supplier>>> GetSuppliers(
        [FromQuery] ListQuery query, CancellationToken cancellationToken)
    {
        return Ok(await suppliersService.GetPageAsync(query.ToPageRequest(), cancellationToken));
    }

    /// <summary>Returns one supplier.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Supplier>> GetSupplier(int id, CancellationToken cancellationToken)
    {
        var supplier = await suppliersService.GetByIdAsync(id, cancellationToken);

        if (supplier is null)
        {
            return NotFound();
        }

        return Ok(supplier);
    }

    /// <summary>Returns the items supplied by a supplier.</summary>
    [HttpGet("{id:int}/items")]
    public async Task<ActionResult<IEnumerable<Item>>> GetSupplierItems(
        int id, CancellationToken cancellationToken)
    {
        var items = await suppliersService.GetItemsAsync(id, cancellationToken);
        return Ok(items);
    }

    /// <summary>Creates a supplier.</summary>
    [HttpPost]
    public async Task<ActionResult<Supplier>> CreateSupplier(Supplier supplier, CancellationToken cancellationToken)
    {
        try
        {
            var created = await suppliersService.CreateAsync(supplier, cancellationToken);

            return CreatedAtAction(nameof(GetSupplier), new { id = created.Id }, created);
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }

    /// <summary>Updates a supplier.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateSupplier(int id, Supplier supplier, CancellationToken cancellationToken)
    {
        try
        {
            return await suppliersService.UpdateAsync(id, supplier, cancellationToken) ? NoContent() : NotFound();
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }

    /// <summary>Deletes a supplier; returns 409 if the supplier still has items.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteSupplier(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await suppliersService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException e)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: e.Message);
        }
    }
}
