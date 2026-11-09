using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

/// <summary>HTTP endpoints for clients; the logic is in ClientsService.</summary>
[ApiController]
[Route("api/v1/clients")]
public class ClientsController(ClientsService clientsService) : ControllerBase
{
    /// <summary>Returns all clients.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Client>>> GetClients(CancellationToken cancellationToken)
    {
        return Ok(await clientsService.GetAllAsync(cancellationToken));
    }

    /// <summary>Returns one client.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Client>> GetClient(int id, CancellationToken cancellationToken)
    {
        var client = await clientsService.GetByIdAsync(id, cancellationToken);

        if (client is null)
        {
            return NotFound();
        }

        return Ok(client);
    }

    /// <summary>Returns the orders of the client, with their items.</summary>
    [HttpGet("{id:int}/orders")]
    public async Task<ActionResult<IEnumerable<Order>>> GetClientOrders(int id, CancellationToken cancellationToken)
    {
        var orders = await clientsService.GetOrdersAsync(id, cancellationToken);

        if (orders is null)
        {
            return NotFound();
        }

        return Ok(orders);
    }

    /// <summary>Creates a client.</summary>
    [HttpPost]
    public async Task<ActionResult<Client>> CreateClient(Client client, CancellationToken cancellationToken)
    {
        try
        {
            var created = await clientsService.CreateAsync(client, cancellationToken);

            return CreatedAtAction(nameof(GetClient), new { id = created.Id }, created);
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>Updates a client.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateClient(int id, Client client, CancellationToken cancellationToken)
    {
        try
        {
            return await clientsService.UpdateAsync(id, client, cancellationToken) ? NoContent() : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>Deletes a client; returns 409 if the client still has orders.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteClient(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await clientsService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException e)
        {
            return Conflict(e.Message);
        }
    }
}
