using System.Text.Json;
using System.Text.Json.Nodes;

using ModernForms.Domain.Schema;

using Xunit;

namespace ModernForms.Tests.Domain;

/// <summary>
/// The schema document contract: parse/serialize round trip, payload
/// validation rules, and the workflow snapshot invariants that
/// usp_SnapshotWorkflow used to enforce procedurally.
/// </summary>
public sealed class FieldSchemaTests
{
    private const string Schema =
        """{"fields":[{"key":"category","label":"Category","type":"select","required":true,"options":["Travel","Meals"]},{"key":"amount","label":"Amount (EGP)","type":"number","required":true,"min":0,"max":10000},{"key":"receiptRef","label":"Receipt reference","type":"text","required":true,"maxLength":60},{"key":"notes","label":"Notes","type":"multiline","required":false,"maxLength":500}],"workflow":{"steps":[{"sequence":1,"role":"supervisor"},{"sequence":2,"role":"admin"}]}}""";

    // ── parse / serialize round trip ─────────────────────────────────────────

    [Fact]
    public void Parse_and_serialize_round_trip()
    {
        var document = SchemaParser.Parse(Schema);

        Assert.Equal(4, document.Fields.Count);
        Assert.Equal(2, document.Workflow.Steps.Count);
        Assert.Equal(1, document.Workflow.Steps[0].Sequence);
        Assert.Equal("supervisor", document.Workflow.Steps[0].Role);
        Assert.Equal("admin", document.Workflow.Steps[1].Role);

        var json = SchemaParser.Serialize(document);
        var reparsed = SchemaParser.Parse(json);

        Assert.Equal(document.Fields.Count, reparsed.Fields.Count);
        Assert.Equal(
            document.Workflow.Steps.Select(s => (s.Sequence, s.Role)),
            reparsed.Workflow.Steps.Select(s => (s.Sequence, s.Role)));
    }

    [Fact]
    public void Empty_input_yields_empty_document()
    {
        var document = SchemaParser.Parse("   ");

        Assert.Empty(document.Fields);
        Assert.Empty(document.Workflow.Steps);
        Assert.Null(SchemaParser.ReadPayload(""));
    }

    [Fact]
    public void Select_field_declared_without_options_is_reported()
    {
        var failures = FormSchemaRules.CheckSchema(
            """{"fields":[{"key":"category","label":"Category","type":"select","required":true,"options":[]}],"workflow":{"steps":[{"sequence":1,"role":"supervisor"}]}}""");

        Assert.Contains(failures, f => f.Contains("no options"));
    }

    [Fact]
    public void Broken_json_is_reported_not_thrown()
    {
        var failures = FormSchemaRules.CheckSchema("{ not json");

        Assert.Contains(failures, f => f.StartsWith("Schema is not valid JSON", StringComparison.Ordinal));
    }

    // ── payload validation ───────────────────────────────────────────────────

    [Fact]
    public void Missing_required_fields_are_reported()
    {
        var document = SchemaParser.Parse(Schema);
        var payload = SchemaParser.ReadPayload("""{"category":"Travel"}""")!;

        var failures = FormSchemaRules.ValidatePayload(document.Fields, payload);

        Assert.Contains(failures, f => f.Contains("'Amount (EGP)' is required"));
        Assert.Contains(failures, f => f.Contains("'Receipt reference' is required"));
        Assert.DoesNotContain(failures, f => f.Contains("'Notes'"));
    }

    [Fact]
    public void Unknown_payload_keys_are_rejected()
    {
        var document = SchemaParser.Parse(Schema);
        var payload = SchemaParser.ReadPayload(
            """{"category":"Travel","amount":100,"receiptRef":"R-1","notes":null,"bonus":900}""")!;

        var failures = FormSchemaRules.ValidatePayload(document.Fields, payload);

        Assert.Contains(failures, f => f.Contains("Unknown field 'bonus'"));
    }

    [Fact]
    public void Select_option_out_of_dictionary_is_rejected()
    {
        var document = SchemaParser.Parse(Schema);
        var payload = SchemaParser.ReadPayload(
            """{"category":"Holidays","amount":100,"receiptRef":"R-1"}""")!;

        var failures = FormSchemaRules.ValidatePayload(document.Fields, payload);

        Assert.Contains(failures, f => f.Contains("must be one of"));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(10001)]
    public void Number_outside_bounds_is_rejected(double amount)
    {
        var document = SchemaParser.Parse(Schema);
        var payload = SchemaParser.ReadPayload(
            $$"""{"category":"Travel","amount":{{amount}},"receiptRef":"R-1"}""")!;

        var failures = FormSchemaRules.ValidatePayload(document.Fields, payload);

        Assert.Contains(failures, f => f.Contains("must be at least") || f.Contains("must be at most"));
    }

    [Fact]
    public void Non_number_text_for_number_field_is_rejected()
    {
        var document = SchemaParser.Parse(Schema);
        var payload = SchemaParser.ReadPayload(
            """{"category":"Travel","amount":"hundred","receiptRef":"R-1"}""")!;

        var failures = FormSchemaRules.ValidatePayload(document.Fields, payload);

        Assert.Contains(failures, f => f.Contains("must be a number"));
    }

    [Fact]
    public void Text_over_max_length_is_rejected()
    {
        var document = SchemaParser.Parse(Schema);
        var payload = SchemaParser.ReadPayload(
            """{"category":"Travel","amount":100,"receiptRef":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")!;

        var failures = FormSchemaRules.ValidatePayload(document.Fields, payload);

        Assert.Contains(failures, f => f.Contains("'Receipt reference' must be at most 60"));
    }

    [Fact]
    public void Date_out_of_format_is_rejected()
    {
        var schema = """{"fields":[{"key":"fromDate","label":"From","type":"date","required":true}],"workflow":{"steps":[{"sequence":1,"role":"supervisor"}]}}""";
        var document = SchemaParser.Parse(schema);
        var payload = SchemaParser.ReadPayload("""{"fromDate":"31/12/2026"}""")!;

        var failures = FormSchemaRules.ValidatePayload(document.Fields, payload);

        Assert.Contains(failures, f => f.Contains("yyyy-MM-dd"));
    }

    [Fact]
    public void Valid_payload_has_no_failures()
    {
        var document = SchemaParser.Parse(Schema);
        var payload = SchemaParser.ReadPayload(
            """{"category":"Travel","amount":1840.5,"receiptRef":"TRV-2026-0042","notes":"Client visit."}""")!;

        var failures = FormSchemaRules.ValidatePayload(document.Fields, payload);

        Assert.Empty(failures);
    }

    [Fact]
    public void Missing_payload_object_is_invalid()
    {
        var document = SchemaParser.Parse(Schema);

        var failures = FormSchemaRules.ValidatePayload(document.Fields, null);

        Assert.Contains(failures, f => f.Contains("Payload is required"));
    }

    // ── workflow snapshot rules ──────────────────────────────────────────────

    [Fact]
    public void Snapshot_orders_steps_1_to_N()
    {
        var document = SchemaParser.Parse(Schema);

        var steps = FormSchemaRules.SnapshotSteps(document.Workflow.Steps);

        Assert.Equal(
            new List<(short StepSequence, string RoleRequired)> { (1, "supervisor"), (2, "admin") },
            steps.Select(s => (s.StepSequence, s.RoleRequired)).ToList());
    }

    [Fact]
    public void Sequence_gap_is_rejected_loudly()
    {
        var steps = new List<WorkflowStepDefinition> { new() { Sequence = 1, Role = "supervisor" }, new() { Sequence = 3, Role = "admin" } };

        var failure = Assert.Throws<FormSchemaRules.SchemaRuleException>(() => FormSchemaRules.SnapshotSteps(steps));

        Assert.Contains("1..N", failure.Message);
    }

    [Fact]
    public void Employee_gated_step_is_rejected()
    {
        var steps = new List<WorkflowStepDefinition> { new() { Sequence = 1, Role = "employee" } };

        var failure = Assert.Throws<FormSchemaRules.SchemaRuleException>(() => FormSchemaRules.SnapshotSteps(steps));

        Assert.Contains("supervisor' or 'admin", failure.Message);
    }

    [Fact]
    public void Stepless_template_is_rejected()
    {
        var failure = Assert.Throws<FormSchemaRules.SchemaRuleException>(
            () => FormSchemaRules.SnapshotSteps(new List<WorkflowStepDefinition>()));

        Assert.Contains("no workflow steps", failure.Message);
    }

    [Fact]
    public void Check_schema_reports_workflow_gaps()
    {
        var failures = FormSchemaRules.CheckSchema(
            """{"fields":[{"key":"reason","label":"Reason","type":"text","required":false}],"workflow":{"steps":[{"sequence":2,"role":"supervisor"}]}}""");

        Assert.Contains(failures, f => f.Contains("1..N"));
    }
}
