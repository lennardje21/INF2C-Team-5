using CargoHub.Api.Authentication;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

// Open to every account; what it may see and do follows from its role (see AccountPolicy).
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

	// POST: api/v2/accounts
	// The response is the only time the new account's API key is shown.
    [HttpPost]
    public async Task<ActionResult<CreatedAccount>> CreateAccount(
        Account account, CancellationToken cancellationToken)
    {
        try
        {
            var created = await accountsService.CreateAsync(Caller, account, cancellationToken);

            return CreatedAtAction(nameof(GetAccount), new { id = created.Id }, created);
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
        catch (ForbiddenException e)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, detail: e.Message);
        }
    }

	// PUT: api/v2/accounts/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAccount(
        int id, Account account, CancellationToken cancellationToken)
    {
        try
        {
            return await accountsService.UpdateAsync(Caller, id, account, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
        catch (ForbiddenException e)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, detail: e.Message);
        }
    }

	// DELETE: api/v2/accounts/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAccount(
        int id, CancellationToken cancellationToken)
    {
        try
        {
            return await accountsService.DeleteAsync(Caller, id, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ForbiddenException e)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, detail: e.Message);
        }
    }

	// POST: api/v2/accounts/{id}/api_key
	// Replaces the account's API key; the response is the only time the new key is shown.
    [HttpPost("{id:int}/api_key")]
    public async Task<ActionResult<IssuedApiKey>> IssueApiKey(
        int id, CancellationToken cancellationToken)
    {
        try
        {
            var issued = await accountsService.IssueApiKeyAsync(Caller, id, cancellationToken);

            if (issued is null)
            {
                return NotFound();
            }

            return Ok(issued);
        }
        catch (ForbiddenException e)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, detail: e.Message);
        }
    }
}
