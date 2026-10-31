using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ModernForms.Application.Common;
using ModernForms.Application.Contracts;
using ModernForms.Application.Forms;

namespace ModernForms.Api.Controllers;

/// <summary>
/// The submissions module — the strangler programme's first fully modernized
/// slice. Codes surfaced through ProblemDetails mirror the legacy FORMS:*
/// error contract (see ExceptionHandlingMiddleware).
/// </summary>
[ApiController]
[Route("api/submissions")]
[Authorize]
[Produces("application/json")]
public sealed class SubmissionsController(IFormService forms, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Paged catalogue. Employees narrow to their own rows; staff see all (optionally mine=true).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<SubmissionListItemDto>>> List(
        [FromQuery] string? status,
        [FromQuery] bool? mine,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default) =>
        Ok(await forms.ListSubmissionsAsync(currentUser, status, mine, page, pageSize, ct));

    /// <summary>One submission detail (header + steps + events) with read-access enforcement.</summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<SubmissionDetailDto>> Get(long id, CancellationToken ct) =>
        Ok(await forms.GetSubmissionAsync(id, currentUser, ct));

    /// <summary>Creates (and optionally submits) a new submission from a template.</summary>
    [HttpPost]
    public async Task<ActionResult<CreatedSubmissionDto>> Create([FromBody] CreateSubmissionRequest request, CancellationToken ct)
    {
        var created = await forms.CreateAsync(request, currentUser, ct);
        return CreatedAtAction(nameof(Get), new { id = created.SubmissionId }, created);
    }

    /// <summary>The decision engine — approved / rejected / withdrawn with optimistic concurrency.</summary>
    [HttpPost("{id:long}/decide")]
    public async Task<ActionResult<DecisionResultDto>> Decide(long id, [FromBody] DecideRequest request, CancellationToken ct) =>
        Ok(await forms.DecideAsync(id, request, currentUser, ct));

    /// <summary>Queue counters for the dashboard workload widget.</summary>
    [HttpGet("workload")]
    public async Task<ActionResult<IReadOnlyList<WorkloadRowDto>>> Workload(CancellationToken ct) =>
        Ok(await forms.WorkloadAsync(ct));
}
