using ModernForms.Application.Common;

namespace ModernForms.Infrastructure.Common;

/// <summary>UTC clock — the injection seam the tests override with fixed times.</summary>
public sealed class DateTimeProvider : IDateTime
{
    public DateTime UtcNow => DateTime.UtcNow;
}
