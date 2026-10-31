using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using Microsoft.IdentityModel.Tokens;

namespace ModernForms.Api.Auth;

/// <summary>
/// Development session issuer: mints a signed JWT for a configured demo user —
/// standing in for the Azure AD SSO token contract (oid = employee key, roles
/// from the demo profile). Bound to the Auth:Mode=Development configuration;
/// the endpoint is never reachable in production (see AccountController).
/// </summary>
public interface IDevTokenIssuer
{
    string IssueToken(string userName, string externalId, string displayName, string[] roles);
}

public sealed class DevelopmentJwtTokenIssuer(IConfiguration configuration) : IDevTokenIssuer
{
    public string IssueToken(string userName, string externalId, string displayName, string[] roles)
    {
        var dev = configuration.GetSection("Auth:Development").Get<AuthOptions.Development>() ?? new AuthOptions.Development();
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(Math.Max(5, dev.TokenLifetimeMinutes));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userName),
            new("unique_name", displayName),
            new("oid", externalId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        claims.AddRange(roles.Select(role => new Claim("roles", role)));

        var key = new SymmetricSecurityKey(Convert.FromBase64String(dev.SigningKeyBase64));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: dev.Issuer,
            audience: dev.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
