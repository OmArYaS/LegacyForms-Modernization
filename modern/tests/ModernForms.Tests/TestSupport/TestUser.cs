using ModernForms.Domain.Enums;

namespace ModernForms.Tests.TestSupport;

/// <summary>Test principal standing in for the JWT-resolved identity.</summary>
public sealed class TestUser(string employeeKey, EmployeeRole role)
    : ModernForms.Application.Common.ICurrentUser
{
    public static TestUser Employee(string key) => new(key, EmployeeRole.Employee);
    public static TestUser Supervisor => new("usr_mona", EmployeeRole.Supervisor);
    public static TestUser Admin => new("usr_ossama", EmployeeRole.Admin);

    public string EmployeeKey => employeeKey;

    public EmployeeRole Role => role;
}

/// <summary>Fixed clock so timestamps land deterministically in assertions.</summary>
public sealed class FixedClock(DateTime now) : ModernForms.Application.Common.IDateTime
{
    public static readonly FixedClock Default = new(new DateTime(2026, 3, 16, 9, 0, 0, DateTimeKind.Utc));

    public static FixedClock At(int hour) => new(new DateTime(2026, 3, 16, hour, 0, 0, DateTimeKind.Utc));

    public DateTime UtcNow => now;
}
