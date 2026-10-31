using System.Web;
using System.Web.Mvc;

namespace LegacyForms.Web.Controllers
{
    /// <summary>
    /// Eras before explicit identity: the employee id and role ride in the
    /// server-side session, guarded by a lightweight attribute. The modern API
    /// replaces this with signed JWTs and role policies.
    /// </summary>
    public abstract class LegacyControllerBase : Controller
    {
        public long? EmployeeId => Session["EmployeeId"] as long?;
        public string EmployeeName => Session["EmployeeName"] as string;
        public string EmployeeRole => Session["EmployeeRole"] as string;

        /// <summary>True when a signed-in employee is present on the session.</summary>
        protected bool IsAuthenticated => EmployeeId.HasValue;

        /// <summary>Falls back to the login page for anything not permitted.</summary>
        protected ActionResult RequireSession(string role = null)
        {
            if (!IsAuthenticated)
            {
                return RedirectToAction("Login", "Account");
            }

            if (role != null && EmployeeRole != role)
            {
                return new HttpStatusCodeResult(403, "You do not have access to this screen.");
            }

            return null;
        }

        protected void StoreSession(long id, string name, string role)
        {
            Session["EmployeeId"] = id;
            Session["EmployeeName"] = name;
            Session["EmployeeRole"] = role;
        }

        protected void SessionAbandon()
        {
            Session.Clear();
            Session.Abandon();
        }
    }
}
