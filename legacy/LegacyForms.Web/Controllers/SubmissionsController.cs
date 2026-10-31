using System.Collections.Generic;
using System.Web.Mvc;
using MySql.Data.MySqlClient;
using LegacyForms.Web.Data;

namespace LegacyForms.Web.Controllers
{
    /// <summary>The catalogue: list + detail + the decision workflow actions.</summary>
    public class SubmissionsController : LegacyControllerBase
    {
        private readonly FormsGateway _gateway = new FormsGateway();

        [HttpGet]
        public ActionResult Index(string status = null, int page = 1)
        {
            var redirect = RequireSession();
            if (redirect != null) return redirect;

            var model = new SubmissionIndexModel
            {
                StatusFilter = status,
                Page = page < 1 ? 1 : page,
                IsEmployee = EmployeeRole == "employee",
                Items = _gateway.ListSubmissions(EmployeeId.Value, EmployeeRole, status, page, 25)
            };

            return View(model);
        }

        [HttpGet]
        public ActionResult Details(long id)
        {
            var redirect = RequireSession();
            if (redirect != null) return redirect;

            SubmissionDetail detail;
            try
            {
                detail = _gateway.GetSubmission(id, EmployeeId.Value, EmployeeRole);
            }
            catch (System.InvalidOperationException)
            {
                return HttpNotFound();
            }

            ViewBag.CanDecide = CanDecide(detail);
            ViewBag.CanWithdraw = CanWithdraw(detail);

            return View(detail);
        }

        [HttpGet]
        public ActionResult New()
        {
            var redirect = RequireSession();
            if (redirect != null) return redirect;

            return View(_gateway.GetActiveTemplates());
        }

        /// <summary>AJAX decision endpoint the Knockout detail viewmodel posts to.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Decide(long id, string decision, string note, int version)
        {
            var redirect = RequireSession();
            if (redirect != null) return Json(new { ok = false, code = "SESSION_EXPIRED" });

            try
            {
                var newVersion = _gateway.Transition(id, EmployeeId.Value, EmployeeRole, decision, note, version);
                return Json(new { ok = true, version = newVersion, status = decision });
            }
            catch (MySqlException ex) when (ex.ErrorCode == 1644)
            {
                // SIGNAL '45000' ⇒ error 1644 with our FORMS:<CODE> message.
                return Json(new { ok = false, code = ExtractCode(ex.Message) });
            }
        }

        /// <summary>AJAX create endpoint used by the template chooser page.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Create(int templateId, string payloadJson, bool submitNow)
        {
            var redirect = RequireSession();
            if (redirect != null) return Json(new { ok = false, code = "SESSION_EXPIRED" });

            try
            {
                var id = _gateway.CreateSubmission(templateId, EmployeeId.Value, payloadJson, submitNow);
                return Json(new { ok = true, submissionId = id });
            }
            catch (MySqlException ex) when (ex.ErrorCode == 1644)
            {
                return Json(new { ok = false, code = ExtractCode(ex.Message) });
            }
        }

        /// <summary>
        /// Decision rights: an open submission whose first pending step requires
        /// the caller's role (admins may discharge any step). The modern API
        /// mirrors this in the role-based approve policy.
        /// </summary>
        private bool CanDecide(SubmissionDetail detail)
        {
            if (detail.Status != "submitted" && detail.Status != "in_review") return false;
            if (!EmployeeId.HasValue) return false;

            foreach (var step in detail.Steps)
            {
                if (step.Decision != "pending") continue;
                return EmployeeRole == "admin" || EmployeeRole == step.RoleRequired;
            }

            return false;
        }

        /// <summary>Withdrawable only undecied and only before any decision lands.</summary>
        private bool CanWithdraw(SubmissionDetail detail)
        {
            return detail.RequestorId == EmployeeId
                && (detail.Status == "submitted" || detail.Status == "in_review")
                && !HasAnyDecision(detail);
        }

        private static bool HasAnyDecision(SubmissionDetail detail)
        {
            foreach (var step in detail.Steps)
            {
                if (step.DecidedAt.HasValue) return true;
            }

            return false;
        }

        private static string ExtractCode(string message)
        {
            // Message shape: 'FORMS:CODE' — strip any SQL-level decoration after the code.
            var index = message.IndexOf('\r');
            return index > 0 ? message.Substring(0, index) : message;
        }
    }

    public class SubmissionIndexModel
    {
        public string StatusFilter { get; set; }
        public int Page { get; set; }
        public bool IsEmployee { get; set; }
        public IList<SubmissionListItem> Items { get; set; }
    }
}
