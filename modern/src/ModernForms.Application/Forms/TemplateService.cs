using ModernForms.Application.Common;
using ModernForms.Application.Contracts;
using ModernForms.Domain.Schema;
using Microsoft.EntityFrameworkCore;

namespace ModernForms.Application.Forms;

/// <summary>Read model for templates: the directory both UIs list and the SPA renders from.</summary>
public interface ITemplateService
{
    /// <summary>Active templates in presentation order.</summary>
    Task<IReadOnlyList<TemplateDto>> ListActiveAsync(CancellationToken ct = default);

    /// <summary>One template with its schema validated against the current rule set.</summary>
    Task<TemplateDto> GetAsync(string templateKey, CancellationToken ct = default);

    /// <summary>Schema-domain diagnostics for a stored template (used by the admin health screen).</summary>
    Task<IReadOnlyList<string>> InspectAsync(string templateKey, CancellationToken ct = default);
}

public sealed class TemplateService(IFormsDbContext db) : ITemplateService
{
    public async Task<IReadOnlyList<TemplateDto>> ListActiveAsync(CancellationToken ct = default)
    {
        var rows = await db.Templates.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Title)
            .Select(t => new TemplateDto(t.Id, t.TemplateKey, t.Title, t.Description, t.FieldSchemaJson))
            .ToListAsync(ct);

        return rows;
    }

    public async Task<TemplateDto> GetAsync(string templateKey, CancellationToken ct = default)
    {
        var template = await db.Templates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TemplateKey == templateKey, ct)
            ?? throw new NotFoundException($"Template '{templateKey}' does not exist.");

        return new TemplateDto(template.Id, template.TemplateKey, template.Title, template.Description, template.FieldSchemaJson);
    }

    public async Task<IReadOnlyList<string>> InspectAsync(string templateKey, CancellationToken ct = default)
    {
        var template = await GetAsync(templateKey, ct);
        return FormSchemaRules.CheckSchema(template.FieldSchema);
    }
}
