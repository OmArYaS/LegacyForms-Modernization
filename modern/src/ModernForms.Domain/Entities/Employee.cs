namespace ModernForms.Domain.Entities;

/// <summary>
/// One identifiable person in the system. The table predates the modernisation
/// programme (see database/mysql/01_schema.sql); the modern API keeps the
/// integer ids for continuity and adds employee_key as the stable SSO handle.
/// </summary>
public sealed class Employee
{
    /// <summary>Legacy surrogate id (BIGINT in MySQL).</summary>
    public long Id { get; set; }

    /// <summary>Azure AD object-id suffix (usr_*). Survives table migrations.</summary>
    public string EmployeeKey { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Department { get; set; }

    public Enums.EmployeeRole Role { get; set; } = Enums.EmployeeRole.Employee;

    public bool Active { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
