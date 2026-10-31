namespace ModernForms.Application.Common;

/// <summary>Clock abstraction so the workflow rules stay testable.</summary>
public interface IDateTime
{
    DateTime UtcNow { get; }
}
