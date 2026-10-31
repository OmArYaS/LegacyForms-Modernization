using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ModernForms.Api.Authorization;
using ModernForms.Application.Contracts;
using ModernForms.Application.Forms;

namespace ModernForms.Api.Controllers;

/// <summary>
/// Form template directory. The response payload mirrors
/// usp_GetActiveTemplates exactly — the schema document travels verbatim
/// to the renderer, whether that renderer is Knockout or Angular.
/// </summary>
[ApiController]
[Route("api/templates")]
[Authorize]
[Produces("application/json")]
public sealed class TemplatesController(ITemplateService templates) : ControllerBase
{
    /// <summary>Active templates (the new-form chooser).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TemplateDto>>> List(CancellationToken ct) =>
        Ok(await templates.ListActiveAsync(ct));

    /// <summary>One template detail.</summary>
    [HttpGet("{templateKey}")]
    public async Task<ActionResult<TemplateDto>> Get(string templateKey, CancellationToken ct) =>
        Ok(await templates.GetAsync(templateKey, ct));

    /// <summary>Schema diagnostics (admin screen) — reports rule violations the schema carries.</summary>
    [HttpGet("{templateKey}/inspect")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<IReadOnlyList<string>>> Inspect(string templateKey, CancellationToken ct) =>
        Ok(await templates.InspectAsync(templateKey, ct));
}
