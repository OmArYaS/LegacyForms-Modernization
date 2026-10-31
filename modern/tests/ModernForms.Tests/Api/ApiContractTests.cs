using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using ModernForms.Application.Contracts;

using Xunit;

namespace ModernForms.Tests.Api;

/// <summary>
/// End-to-end contract tests through the real ASP.NET Core pipeline (routing,
/// model binding, authentication, authorization, ProblemDetails mapping) with
/// the SQLite demo profile. Each test boots an isolated host with a seeded
/// database — the same migration + seed path a container boot exercises.
/// The FORMS:* codes mirror the legacy procedure contract.
/// </summary>
public sealed class ApiContractTests
{
    // ── authentication & authorization ──────────────────────────────────────

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        await using var host = await TestHost.StartAsync("amira", login: false);

        var response = await host.Client.GetAsync("/api/submissions");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_demo_user_cannot_obtain_a_token()
    {
        await using var host = await TestHost.StartAsync("intruder");

        Assert.Equal("", host.Token);
    }

    // ── seeded reads ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Templates_are_listed_for_the_new_form_chooser()
    {
        await using var host = await TestHost.StartAsync("amira");

        var response = await host.Client.GetAsync("/api/templates");
        var templates = await response.Content.ReadFromJsonAsync<List<TemplateDto>>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, templates!.Count);
        Assert.Contains(templates, t => t.TemplateKey == "leave-request");
        Assert.Contains(templates, t => t.TemplateKey == "expense-claim");
        Assert.Contains(templates, t => t.TemplateKey == "equipment-request");
    }

    [Fact]
    public async Task Employee_listing_sees_only_owned_rows()
    {
        await using var host = await TestHost.StartAsync("amira");

        var response = await host.Client.GetAsync("/api/submissions");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.All(body.RootElement.GetProperty("items").EnumerateArray(), item =>
            Assert.Equal("Amira Hassan", item.GetProperty("requestorName").GetString()));
    }

    [Fact]
    public async Task Employee_cannot_read_another_employees_submission()
    {
        await using var host = await TestHost.StartAsync("amira");

        // Submission 4 in the seed order belongs to Tarek (submitted expense claim).
        var response = await host.Client.GetAsync("/api/submissions/2");

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("NOT_SUBMISSION_OWNER", await TestHost.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Workload_reports_the_pending_steps_by_gate()
    {
        await using var host = await TestHost.StartAsync("mona");

        var response = await host.Client.GetAsync("/api/submissions/workload");
        var rows = await response.Content.ReadFromJsonAsync<List<WorkloadRowDto>>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(rows!, r => r.Status == "submitted" && r.RoleRequired == "supervisor" && r.PendingSubmissions == 1);
        Assert.Contains(rows!, r => r.Status == "submitted" && r.RoleRequired == "admin" && r.PendingSubmissions == 1);
    }

    // ── create flow ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Submitted_request_snapshots_steps_and_is_locatable()
    {
        await using var host = await TestHost.StartAsync("amira");

        var response = await host.Client.PostAsJsonAsync("/api/submissions", new
        {
            templateKey = "leave-request",
            payloadJson = """{"leaveType":"Sick","fromDate":"2026-06-01","toDate":"2026-06-02","reason":"Doctor visit"}""",
            submitNow = true
        });

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedSubmissionDto>();
        Assert.Equal("submitted", created!.Status);
        Assert.Equal(1, created.Version);

        var detail = await host.Client.GetAsync($"/api/submissions/{created.SubmissionId}");
        var document = await detail.Content.ReadFromJsonAsync<SubmissionDetailDto>();
        Assert.Single(document!.Steps);
        Assert.Equal("supervisor", document.Steps[0].RoleRequired);
        Assert.Equal("pending", document.Steps[0].Decision);
        Assert.Contains(document.Events, e => e.EventType == "submitted");
    }

    [Fact]
    public async Task Schema_violations_surface_as_INVALID_PAYLOAD()
    {
        await using var host = await TestHost.StartAsync("amira");

        var response = await host.Client.PostAsJsonAsync("/api/submissions", new
        {
            templateKey = "leave-request",
            payloadJson = """{"leaveType":"Golf"}""",
            submitNow = true
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_PAYLOAD", await TestHost.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Retired_template_is_not_creatable()
    {
        await using var host = await TestHost.StartAsync("amira");

        var response = await host.Client.PostAsJsonAsync("/api/submissions", new
        {
            templateKey = "leave-vacation-request-archived",
            payloadJson = "{}",
            submitNow = true
        });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("TEMPLATE_NOT_FOUND", await TestHost.ProblemCodeAsync(response));
    }

    // ── decision matrix ──────────────────────────────────────────────────────

    [Fact]
    public async Task Supervisor_and_admin_complete_the_two_step_expense_flow()
    {
        await using var host = await TestHost.StartAsync("mona");

        // Tarek's seeded expense claim awaits the supervisor step (version 1).
        var first = await host.Client.PostAsJsonAsync("/api/submissions/2/decide", new
        {
            decision = "approved",
            note = "Receipts checked.",
            seenVersion = 1
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, first.StatusCode);
        var stepOne = await first.Content.ReadFromJsonAsync<DecisionResultDto>();
        Assert.Equal("in_review", stepOne!.Status);
        Assert.Equal(2, stepOne.NewVersion);

        // Same host, second reviewer: mint the admin token and switch identity.
        var adminToken = await host.Client.PostAsJsonAsync("/api/auth/token", new { userName = "ossama" });
        var admin = (await adminToken.Content.ReadFromJsonAsync<TokenResponse>())!;
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        // Staleness trap: acting on version 1 must lose loudly.
        var stale = await host.Client.PostAsJsonAsync("/api/submissions/2/decide", new
        {
            decision = "approved",
            note = "Finance ok.",
            seenVersion = 1
        });

        Assert.Equal(System.Net.HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("CONCURRENT_UPDATE", await TestHost.ProblemCodeAsync(stale));

        // Arrival with the fresh version closes the submission.
        var second = await host.Client.PostAsJsonAsync("/api/submissions/2/decide", new
        {
            decision = "approved",
            note = "Finance ok.",
            seenVersion = 2
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, second.StatusCode);
        var stepTwo = await second.Content.ReadFromJsonAsync<DecisionResultDto>();
        Assert.Equal("approved", stepTwo!.Status);
        Assert.Equal(3, stepTwo.NewVersion);
    }

    [Fact]
    public async Task Employee_cannot_record_a_review_decision()
    {
        await using var host = await TestHost.StartAsync("amira");

        var response = await host.Client.PostAsJsonAsync("/api/submissions/2/decide", new
        {
            decision = "approved",
            note = "Self service.",
            seenVersion = 1
        });

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ROLE_MISMATCH", await TestHost.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Draft_cannot_route_through_the_decision_engine()
    {
        await using var host = await TestHost.StartAsync("amira");

        // Amira's seeded draft (a leave request, never submitted).
        var response = await host.Client.PostAsJsonAsync("/api/submissions/4/decide", new
        {
            decision = "approved",
            note = (string?)null,
            seenVersion = 1
        });

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("TERMINAL_STATE", await TestHost.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Requestor_can_withdraw_a_pending_submission()
    {
        await using var host = await TestHost.StartAsync("amira");

        // First create a fresh pending submission so the seed stays untouched.
        var created = await host.Client.PostAsJsonAsync("/api/submissions", new
        {
            templateKey = "leave-request",
            payloadJson = """{"leaveType":"Annual","fromDate":"2026-07-06","toDate":"2026-07-10","reason":"Wedding"}""",
            submitNow = true
        });
        var submission = await created.Content.ReadFromJsonAsync<CreatedSubmissionDto>();

        var response = await host.Client.PostAsJsonAsync(
            $"/api/submissions/{submission!.SubmissionId}/decide",
            new { decision = "withdrawn", note = "Wedding postponed.", seenVersion = 1 });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DecisionResultDto>();
        Assert.Equal("withdrawn", result!.Status);
        Assert.Equal(2, result.NewVersion);
    }

    // ── RBAC gates ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Template_inspection_is_admin_only()
    {
        await using var supervisor = await TestHost.StartAsync("mona");
        var forbidden = await supervisor.Client.GetAsync("/api/templates/leave-request/inspect");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, forbidden.StatusCode);

        await using var admin = await TestHost.StartAsync("ossama");
        var allowed = await admin.Client.GetAsync("/api/templates/leave-request/inspect");
        Assert.Equal(System.Net.HttpStatusCode.OK, allowed.StatusCode);
        var findings = await allowed.Content.ReadFromJsonAsync<List<string>>();
        Assert.Empty(findings!);
    }

    [Fact]
    public async Task Health_reports_ready()
    {
        var factory = new ModernFormsApiFactory();
        await using var _ = factory;

        var response = await factory.CreateClient().GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

    /// <summary>A booted host: isolated DB + a client logged in as the demo user.</summary>
    private sealed class TestHost(ModernFormsApiFactory factory, HttpClient client, string token) : IAsyncDisposable
    {
        public ModernFormsApiFactory Factory => factory;

        public HttpClient Client => client;

        public string Token => token;

        public static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.TryGetProperty("code", out var code)
                ? code.GetString() ?? ""
                : "";
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await factory.DisposeAsync();
        }

        public static async Task<TestHost> StartAsync(string user, bool login = true)
        {
            var factory = new ModernFormsApiFactory();
            var client = factory.CreateClient();

            if (!login)
            {
                return new TestHost(factory, client, "");
            }

            var tokenResponse = await client.PostAsJsonAsync("/api/auth/token", new { userName = user });

            if (!tokenResponse.IsSuccessStatusCode)
            {
                // Failed login: host stays open so the caller can inspect responses (and dispose normally).
                return new TestHost(factory, client, "");
            }

            var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
            Assert.NotNull(token);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

            return new TestHost(factory, client, token.AccessToken);
        }
    }
}
