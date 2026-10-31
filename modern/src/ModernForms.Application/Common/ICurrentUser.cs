using ModernForms.Domain.Enums;

namespace ModernForms.Application.Common;

/// <summary>
/// Identity of the caller resolved from the bearer token (dev-stub JWT in
/// demos; Azure AD SSO in production — docs/migration-strategy.md covers the
/// end of the legacy session-cookie world).
/// </summary>
public interface ICurrentUser
{
    /// <summary>Stable employee key (usr_*) — survives re-provisioning.</summary>
    string EmployeeKey { get; }

    /// <summary>Resolved role from token claims; Unknown when absent.</summary>
    EmployeeRole Role { get; }
}
