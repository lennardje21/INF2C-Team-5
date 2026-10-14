using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

[ApiController]
[Route("api/v1/warehouses")]
public class WarehousesController(WarehousesService warehousesService) : ControllerBase
{
	// GET: api/v1/warehouses
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Warehouse>>> GetWarehouses(
        CancellationToken cancellationToken)
    {
        return Ok(await warehousesService.GetAllAsync(cancellationToken));
    }

	// GET: api/v1/warehouses/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Warehouse>> GetWarehouse(
        int id, CancellationToken cancellationToken)
    {
        var warehouse = await warehousesService.GetByIdAsync(id, cancellationToken);

        if (warehouse is null)
        {
            return NotFound();
        }

        return Ok(warehouse);
    }

	// GET: api/v1/warehouses/{id}/locations
    [HttpGet("{id:int}/locations")]
    public async Task<ActionResult<IEnumerable<Location>>> GetWarehouseLocations(
        int id, CancellationToken cancellationToken)
    {
        var locations = await warehousesService.GetLocationsAsync(id, cancellationToken);

        if (locations is null)
        {
            return NotFound();
        }

        return Ok(locations);
    }

	// POST: api/v1/warehouses
    [HttpPost]
    public async Task<ActionResult<Warehouse>> CreateWarehouse(
        Warehouse warehouse, CancellationToken cancellationToken)
    {
        try
        {
            var created = await warehousesService.CreateAsync(warehouse, cancellationToken);

            return CreatedAtAction(nameof(GetWarehouse), new { id = created.Id }, created);
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

	// PUT: api/v1/warehouses/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateWarehouse(
        int id, Warehouse warehouse, CancellationToken cancellationToken)
    {
        try
        {
            return await warehousesService.UpdateAsync(id, warehouse, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

	// DELETE: api/v1/warehouses/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteWarehouse(
        int id, CancellationToken cancellationToken)
    {
        try
        {
            return await warehousesService.DeleteAsync(id, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }
}