using CargoHub.Api.Authentication;
using CargoHub.Api.Services;
using CargoHub.Api.Settings;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

// Open to every account at the API key level; what it may do follows from its role: only
// an admin, root or superroot may read or change the blockade detection times.
[ApiController]
[Route("api/v2/blockade-settings")]
public class BlockadeSettingsController(IBlockadeSettingsService settingsService) : ControllerBase
{
    // Set by ApiKeyMiddleware for every request that reaches a controller.
    private ApiUser Caller => (ApiUser)HttpContext.Items[typeof(ApiUser)]!;

    private bool MayConfigure =>
        Caller.Role == AccountRoles.Admin || AccountRoles.IsAbove(Caller.Role, AccountRoles.Admin);

    private ObjectResult NotAllowed() => Problem(
        statusCode: StatusCodes.Status403Forbidden,
        detail: "Only an admin or a higher role may use the blockade settings.");

    // GET: api/v2/blockade-settings
    [HttpGet]
    public async Task<ActionResult<BlockadeSettings>> GetSettings(CancellationToken cancellationToken)
    {
        if (!MayConfigure)
            return NotAllowed();

        return Ok(await settingsService.GetAsync(cancellationToken));
    }

    // PUT: api/v2/blockade-settings
    // Replaces all four values; they apply to the whole system.
    [HttpPut]
    public async Task<ActionResult<BlockadeSettings>> UpdateSettings(
        BlockadeSettings settings, CancellationToken cancellationToken)
    {
        if (!MayConfigure)
            return NotAllowed();

        try
        {
            return Ok(await settingsService.UpdateAsync(settings, cancellationToken));
        }
        catch (ArgumentException e)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: e.Message);
        }
    }
}
