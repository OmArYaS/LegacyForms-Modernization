using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using ModernForms.Application.Common;
using ModernForms.Domain.Enums;

namespace ModernForms.Infrastructure.Security;

/// <summary>
/// Resolves the caller identity from JWT claims. The dev issuer carries
/// employee_key in the `oid` claim and the role list in `roles`; the Azure
/// AD SSO integration uses the same handles (docs/migration-strategy.md).
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string EmployeeKey =>
        accessor.HttpContext?.User?.FindFirst("oid")?.Value ?? string.Empty;

    /// <summary>First recognized role claim wins; employee rights are the fallback floor.</summary>
    public EmployeeRole Role
    {
        get
        {
            var claims = accessor.HttpContext?.User?.Claims
                .Where(c => c.Type == ClaimTypes.Role || c.Type == "roles")
                .Select(c => c.Value)
                .ToList() ?? new List<string>();

            return claims.Contains("admin", StringComparer.OrdinalIgnoreCase) ? EmployeeRole.Admin
                : claims.Contains("supervisor", StringComparer.OrdinalIgnoreCase) ? EmployeeRole.Supervisor
                : EmployeeRole.Employee;
        }
    }
}
