using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

// HTTP endpoints for locations; the logic is in LocationsService.
[ApiController]
[Route("api/v1/locations")]
public class LocationsController(LocationsService locationsService) : ControllerBase
{
    // Returns all locations.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Location>>> GetLocations(
        CancellationToken cancellationToken)
    {
        return Ok(await locationsService.GetAllAsync(cancellationToken));
    }

    // Returns one location.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Location>> GetLocation(
        int id, CancellationToken cancellationToken)
    {
        var location = await locationsService.GetByIdAsync(id, cancellationToken);

        if (location is null)
        {
            return NotFound();
        }

        return Ok(location);
    }

    // Creates a location.
    [HttpPost]
    public async Task<ActionResult<Location>> CreateLocation(
        Location location, CancellationToken cancellationToken)
    {
        try
        {
            var created = await locationsService.CreateAsync(location, cancellationToken);

            return CreatedAtAction(nameof(GetLocation), new { id = created.Id }, created);
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    // Updates a location.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateLocation(
        int id, Location location, CancellationToken cancellationToken)
    {
        try
        {
            return await locationsService.UpdateAsync(id, location, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    // Deletes a location; returns 400 if it is still referenced.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteLocation(
        int id, CancellationToken cancellationToken)
    {
        try
        {
            return await locationsService.DeleteAsync(id, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }
}