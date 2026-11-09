using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

/// <summary>HTTP endpoints for suppliers; the logic is in SuppliersService.</summary>
[ApiController]
[Route("api/v1/suppliers")]
public class SuppliersController(SuppliersService suppliersService) : ControllerBase
{
    /// <summary>Returns all suppliers.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Supplier>>> GetSuppliers(CancellationToken cancellationToken)
    {
        return Ok(await suppliersService.GetAllAsync(cancellationToken));
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
            return BadRequest(e.Message);
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
            return BadRequest(e.Message);
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
            return Conflict(e.Message);
        }
    }
}
