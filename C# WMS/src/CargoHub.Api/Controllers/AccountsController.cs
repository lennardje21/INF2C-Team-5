using CargoHub.Api.Authentication;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

// Open to every account; what it gets to see follows from its role (see AccountPolicy).
[ApiController]
[Route("api/v2/accounts")]
public class AccountsController(AccountsService accountsService) : ControllerBase
{
    // Set by ApiKeyMiddleware for every request that reaches a controller.
    private ApiUser Caller => (ApiUser)HttpContext.Items[typeof(ApiUser)]!;

	// GET: api/v2/accounts
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Account>>> GetAccounts(
        CancellationToken cancellationToken)
    {
        return Ok(await accountsService.GetAllAsync(Caller, cancellationToken));
    }

	// GET: api/v2/accounts/me
    [HttpGet("me")]
    public Task<ActionResult<Account>> GetOwnAccount(CancellationToken cancellationToken)
    {
        return GetAccount(Caller.AccountId, cancellationToken);
    }

	// GET: api/v2/accounts/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Account>> GetAccount(
        int id, CancellationToken cancellationToken)
    {
        var account = await accountsService.GetByIdAsync(Caller, id, cancellationToken);

        if (account is null)
        {
            return NotFound();
        }

        return Ok(account);
    }
}
