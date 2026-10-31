using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using ModernForms.Application.Common;

namespace ModernForms.Api.Middleware;

/// <summary>
/// Maps the workflow-level exceptions to RFC 9457 ProblemDetails. The codes
/// mirror the legacy FORMS:* procedure messages so both the legacy jQuery
/// handlers and the modern SPA map errors through the same dictionary.
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions ProblemJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BusinessRuleException ex)
        {
            await WriteProblemAsync(
                context,
                ex.StatusCode,
                ex.Code,
                CodeTitles.For(ex.Code),
                ex.Message);
        }
        catch (NotFoundException ex)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status404NotFound,
                "RESOURCE_NOT_FOUND",
                "Not found",
                ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status409Conflict,
                "CONCURRENT_UPDATE",
                CodeTitles.For("CONCURRENT_UPDATE"),
                "The record changed while you were saving — reload and retry.");
        }
        catch (DbUpdateException ex)
        {
#pragma warning disable CA1848 // one cold-path warning per store conflict — LoggerMessage would be ceremony here
            logger.LogWarning(ex, "Store-level conflict surfaced as a client-facing conflict.");
#pragma warning restore CA1848
            await WriteProblemAsync(
                context,
                StatusCodes.Status409Conflict,
                "STORE_CONFLICT",
                "Conflict",
                "The database rejected the write — likely a constraint violation.");
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context, int statusCode, string code, string title, string detail)
    {
        if (context.Response.HasStarted)
        {
            return; // too late to reshape — the write is already on the wire
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        problem.Extensions["code"] = code;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, ProblemJson));
    }
}

/// <summary>Stable sentence titles for the FORMS-era business codes.</summary>
public static class CodeTitles
{
    public static string For(string code) => code switch
    {
        "CONCURRENT_UPDATE" => "Concurrent modification",
        "ROLE_MISMATCH" => "Role mismatch",
        "NOT_SUBMISSION_OWNER" => "Not the submission owner",
        "TERMINAL_STATE" => "Submission already closed",
        "NO_PENDING_STEP" => "No pending approval step",
        "WITHDRAW_TOO_LATE" => "Withdraw no longer possible",
        "TEMPLATE_NOT_FOUND" => "Template not found",
        "SUBMISSION_NOT_FOUND" => "Submission not found",
        "INVALID_DECISION" => "Invalid decision",
        "INVALID_PAYLOAD" => "Payload rejected",
        "REQUESTOR_INACTIVE" => "Requestor inactive",
        "UNKNOWN_USER" => "Unknown user",
        _ => "Request failed"
    };
}
