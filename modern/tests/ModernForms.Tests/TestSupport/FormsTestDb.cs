using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using ModernForms.Application.Forms;
using ModernForms.Domain.Entities;
using ModernForms.Domain.Enums;
using ModernForms.Infrastructure.Persistence;
using ModernForms.Tests.TestSupport;

namespace ModernForms.Tests.TestSupport;

/// <summary>
/// The unit-test persistence host: a real relational Sqlite database held
/// open in memory (same provider the API runs — no InMemory dual-provider
/// games), schema from EnsureCreated, and the minimal demo dataset the
/// workflow mirrors need.
/// </summary>
public sealed class FormsTestDb : IAsyncDisposable
{
    public const string MinimalSchema =
        """{"fields":[{"key":"leaveType","label":"Leave type","type":"select","required":true,"options":["Annual","Sick"]},{"key":"reason","label":"Reason","type":"multiline","required":true,"maxLength":500}],"workflow":{"steps":[{"sequence":1,"role":"supervisor"}]}}""";

    public const string TwoStepSchema =
        """{"fields":[{"key":"amount","label":"Amount (EGP)","type":"number","required":true,"min":0},{"key":"receiptRef","label":"Receipt reference","type":"text","required":true,"maxLength":60}],"workflow":{"steps":[{"sequence":1,"role":"supervisor"},{"sequence":2,"role":"admin"}]}}""";

    private readonly SqliteConnection connection;

    private FormsTestDb(SqliteConnection connection, FormsDbContext db)
    {
        this.connection = connection;
        Db = db;
    }

    public FormsDbContext Db { get; }

    public static async Task<FormsTestDb> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<FormsDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new FormsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        return new FormsTestDb(connection, db);
    }

    /// <summary>Seeds the users + two templates; returns their row ids.</summary>
    public async Task<SeedResult> SeedAsync(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;

        var amira = new Employee { EmployeeKey = "usr_amira", FullName = "Amira Hassan", Role = EmployeeRole.Employee, Active = true, CreatedAtUtc = now };
        var tarek = new Employee { EmployeeKey = "usr_tarek", FullName = "Tarek Fahmy", Role = EmployeeRole.Employee, Active = true, CreatedAtUtc = now };
        var mona = new Employee { EmployeeKey = "usr_mona", FullName = "Mona Sharif", Role = EmployeeRole.Supervisor, Active = true, CreatedAtUtc = now };
        var ossama = new Employee { EmployeeKey = "usr_ossama", FullName = "Ossama Nabil", Role = EmployeeRole.Admin, Active = true, CreatedAtUtc = now };

        var leaveTemplate = new FormTemplate { TemplateKey = "leave-request", Title = "Leave request", FieldSchemaJson = MinimalSchema, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var expenseTemplate = new FormTemplate { TemplateKey = "expense-claim", Title = "Expense claim", FieldSchemaJson = TwoStepSchema, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var retiredTemplate = new FormTemplate { TemplateKey = "old-form", Title = "Old form", FieldSchemaJson = MinimalSchema, IsActive = false, CreatedAtUtc = now, UpdatedAtUtc = now };

        Db.Employees.AddRange(amira, tarek, mona, ossama);
        Db.Templates.AddRange(leaveTemplate, expenseTemplate, retiredTemplate);
        await Db.SaveChangesAsync();

        return new SeedResult(amira.Id, tarek.Id, mona.Id, ossama.Id, leaveTemplate.Id, expenseTemplate.Id, retiredTemplate.Id);
    }

    public ValueTask DisposeAsync()
    {
        Db.Dispose();
        connection.Dispose();
        return ValueTask.CompletedTask;
    }

    public sealed record SeedResult(
        long AmiraId, long TarekId, long MonaId, long OssamaId,
        int LeaveTemplateId, int ExpenseTemplateId, int RetiredTemplateId);
}
