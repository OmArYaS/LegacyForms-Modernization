using System.Text.Json;

using ModernForms.Api.Auth;
using ModernForms.Api.Authorization;
using ModernForms.Api.Middleware;
using ModernForms.Application;
using ModernForms.Infrastructure;
using ModernForms.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ── Layers ───────────────────────────────────────────────────────────────────
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ── Authentication: Azure AD (prod) or development JWT stub ─────────────────
AuthOptions.ConfigureAuthentication(builder.Services, builder.Configuration);
builder.Services.AddFormsAuthorization();
builder.Services.AddHttpContextAccessor();

// ── Web plumbing ─────────────────────────────────────────────────────────────
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "ModernForms API", Version = "v1" }));

var readyTags = new string[] { "ready" };

builder.Services.AddHealthChecks()
    .AddDbContextCheck<FormsDbContext>("forms-db", tags: readyTags);

var app = builder.Build();

// ── Pipeline ─────────────────────────────────────────────────────────────────
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ModernForms API v1"));
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")
});

// ── Database: apply code-first migrations, then seed demo data ───────────────
await app.UseSeedDemoDataAsync();

app.Run();

/// <summary>Exposes Program for the WebApplicationFactory-based contract tests.</summary>
public partial class Program { }
