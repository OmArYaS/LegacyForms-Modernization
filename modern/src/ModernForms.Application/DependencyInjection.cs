using Microsoft.Extensions.DependencyInjection;

using ModernForms.Application.Common;
using ModernForms.Application.Forms;

namespace ModernForms.Application;

/// <summary>Wires the use-case services.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IFormService, FormService>();
        services.AddScoped<ITemplateService, TemplateService>();
        return services;
    }
}
