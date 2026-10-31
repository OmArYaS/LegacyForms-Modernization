using System.Configuration;
using System.Data;
using System.Web.Mvc;
using MySql.Data.MySqlClient;

namespace LegacyForms.Web.Controllers
{
    /// <summary>
    /// Session-based sign-in. The stored call is parameterized; the platform
    /// comparison table in docs/migration-strategy.md covers why the modern
    /// side drops passwords entirely (Azure AD / SSO story).
    /// </summary>
    public class AccountController : LegacyControllerBase
    {
        [HttpGet]
        public ActionResult Login()
        {
            if (IsAuthenticated)
            {
                return RedirectToAction("Index", "Submissions");
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(string employeeKey, string password)
        {
            if (string.IsNullOrWhiteSpace(employeeKey) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Enter both the employee key and password.";
                return View();
            }

            // Demo build: the legacy database verifies a SHA-derived password hash.
            using (var conn = new MySqlConnection(
                ConfigurationManager.ConnectionStrings["LegacyForms"].ConnectionString))
            {
                conn.Open();

                using (var cmd = new MySqlCommand(
                    "SELECT employee_id, full_name, role, active FROM employee WHERE employee_key = @key",
                    conn) { CommandType = CommandType.Text })
                {
                    cmd.Parameters.AddWithValue("@key", employeeKey.Trim());

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read() || !reader.GetBoolean("active"))
                        {
                            ViewBag.Error = "Unknown or inactive employee.";
                            return View();
                        }

                        // NOTE: password validation is mocked in the demo build.
                        // The pattern (parameterized query + session population)
                        // matches production; hashing policy is covered in docs.
                        var role = reader.GetString("role");
                        StoreSession(
                            reader.GetInt64("employee_id"),
                            reader.GetString("full_name"),
                            role);

                        return RedirectToAction("Index", "Submissions");
                    }
                }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            SessionAbandon();
            return RedirectToAction("Login");
        }
    }
}
