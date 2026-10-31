using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ModernForms.Api.Auth;

namespace ModernForms.Api.Controllers;

/// <summary>
/// Demo identity issuing (dev stub for Azure AD SSO). The endpoint disables
/// itself under Auth:Mode=AzureAd to be unreachable in production; in
/// production the SPA uses MSAL and the SSO token contract (oid + roles).
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AccountController(IDevTokenIssuer tokenIssuer, IConfiguration configuration) : ControllerBase
{
    [HttpPost("token")]
    public IActionResult Token([FromBody] JsonElement body)
    {
        if (IsProductionAuth())
        {
            return NotFound();
        }

        string userName;
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("userName", out var property))
        {
            userName = (property.GetString() ?? string.Empty).Trim();
        }
        else
        {
            return BadRequest(new { code = "INVALID_BODY", detail = "Expected { \"userName\": \"...\" }." });
        }

        var dev = configuration.GetSection("Auth:Development").Get<AuthOptions.Development>() ?? new AuthOptions.Development();

        if (!dev.Users.TryGetValue(userName, out var profile))
        {
            return Unauthorized(new { code = "UNKNOWN_USER", detail = $"Demo user '{userName}' is not configured." });
        }

        var token = tokenIssuer.IssueToken(userName, profile.ExternalId, userName, profile.Roles);
        return Ok(new AuthTokenResponse(token, "Bearer", 3600));
    }

    private bool IsProductionAuth() =>
        string.Equals(configuration["Auth:Mode"], "AzureAd", StringComparison.OrdinalIgnoreCase);

    public sealed record AuthTokenResponse(string AccessToken, string TokenType, int ExpiresIn);
}
