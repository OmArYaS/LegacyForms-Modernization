using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // migration builder arrays are tooling-style one-shots

namespace ModernForms.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// SQLite bootstrap for the demo profile. Column names mirror the legacy
    /// MySQL schema table-for-table and column-for-column so the two data
    /// platforms tell one story (database/mysql/01_schema.sql).
    /// </summary>
    [DbContext(typeof(ModernForms.Infrastructure.Persistence.FormsDbContext))]
    [Migration("20260901000000_InitialCreate")]
    public sealed class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── employee ────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "employee",
                columns: table => new
                {
                    employee_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    employee_key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    full_name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    email = table.Column<string>(type: "TEXT", maxLength: 190, nullable: true),
                    department = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    active = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee", x => x.employee_id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_employee_key",
                table: "employee",
                column: "employee_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_role",
                table: "employee",
                column: "role");

            // ── form_template ───────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "form_template",
                columns: table => new
                {
                    template_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    template_key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    field_schema = table.Column<string>(type: "TEXT", nullable: false),
                    is_active = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_template", x => x.template_id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_template_key",
                table: "form_template",
                column: "template_key",
                unique: true);

            // ── form_submission ─────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "form_submission",
                columns: table => new
                {
                    submission_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    template_id = table.Column<int>(type: "INTEGER", nullable: false),
                    requestor_id = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    closed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    decision_note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_submission", x => x.submission_id);
                    table.ForeignKey(
                        name: "fk_submission_template",
                        column: x => x.template_id,
                        principalTable: "form_template",
                        principalColumn: "template_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submission_requestor",
                        column: x => x.requestor_id,
                        principalTable: "employee",
                        principalColumn: "employee_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_submission_status_submitted",
                table: "form_submission",
                columns: new[] { "status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_submission_requestor",
                table: "form_submission",
                column: "requestor_id");

            // ── approval_step ───────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "approval_step",
                columns: table => new
                {
                    step_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    submission_id = table.Column<long>(type: "INTEGER", nullable: false),
                    step_sequence = table.Column<short>(type: "INTEGER", nullable: false),
                    role_required = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    decision = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    decided_by = table.Column<long>(type: "INTEGER", nullable: true),
                    decided_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_step", x => x.step_id);
                    table.ForeignKey(
                        name: "fk_step_submission",
                        column: x => x.submission_id,
                        principalTable: "form_submission",
                        principalColumn: "submission_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_step_decider",
                        column: x => x.decided_by,
                        principalTable: "employee",
                        principalColumn: "employee_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "uq_step_submission_sequence",
                table: "approval_step",
                columns: new[] { "submission_id", "step_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_step_role_decision",
                table: "approval_step",
                columns: new[] { "role_required", "decision" });

            // ── form_event ──────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "form_event",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    submission_id = table.Column<long>(type: "INTEGER", nullable: false),
                    event_type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    actor_id = table.Column<long>(type: "INTEGER", nullable: true),
                    detail = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_event", x => x.event_id);
                    table.ForeignKey(
                        name: "fk_event_submission",
                        column: x => x.submission_id,
                        principalTable: "form_submission",
                        principalColumn: "submission_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_submission",
                table: "form_event",
                columns: new[] { "submission_id", "occurred_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "form_event");
            migrationBuilder.DropTable(name: "approval_step");
            migrationBuilder.DropTable(name: "form_submission");
            migrationBuilder.DropTable(name: "form_template");
            migrationBuilder.DropTable(name: "employee");
        }
    }
}
