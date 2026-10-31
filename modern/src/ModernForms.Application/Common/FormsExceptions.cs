namespace ModernForms.Application.Common;

/// <summary>
/// A business rule decision the caller can act on (fix and retry, or not).
/// Raised by the workflow services; the API middleware turns CODE + status
/// into ProblemDetails — the codes mirror the legacy FORMS:* messages so the
/// client error handling reads identically across the two eras.
/// </summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string code, string message, int statusCode = 400)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>Stable machine-readable code, e.g. CONCURRENT_UPDATE.</summary>
    public string Code { get; }

    /// <summary>HTTP status the API should surface for this rule.</summary>
    public int StatusCode { get; }
}

/// <summary>Requested resource does not exist (mapped to 404).</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
