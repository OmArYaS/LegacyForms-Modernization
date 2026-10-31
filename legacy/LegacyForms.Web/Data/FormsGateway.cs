using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using MySql.Data.MySqlClient;

namespace LegacyForms.Web.Data
{
    /// <summary>
    /// 2010-era data access: every statement is a stored procedure call, every
    /// argument is a MySqlParameter. No dynamic SQL exists anywhere in this
    /// application. The modern gateway (ModernForms.Infrastructure.Data)
    /// speaks the exact same procedure contract.
    /// </summary>
    public sealed class FormsGateway
    {
        private readonly string _connectionString =
            ConfigurationManager.ConnectionStrings["LegacyForms"].ConnectionString;

        /// <summary>Active form templates for the chooser screens.</summary>
        public IList<TemplateRow> GetActiveTemplates()
        {
            using (var conn = Open())
            using (var cmd = new MySqlCommand("usp_GetActiveTemplates", (MySqlConnection)conn) { CommandType = CommandType.StoredProcedure })
            using (var reader = cmd.ExecuteReader())
            {
                var rows = new List<TemplateRow>();
                while (reader.Read())
                {
                    rows.Add(new TemplateRow
                    {
                        TemplateId = reader.GetInt32("TemplateId"),
                        TemplateKey = reader.GetString("TemplateKey"),
                        Title = reader.GetString("Title"),
                        Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString("Description"),
                        FieldSchema = reader.GetString("FieldSchema")
                    });
                }

                return rows;
            }
        }

        /// <summary>Paged catalogue; employees are narrowed to their own rows by the proc.</summary>
        public IList<SubmissionListItem> ListSubmissions(long employeeId, string role, string status, int page, int pageSize)
        {
            using (var conn = Open())
            using (var cmd = new MySqlCommand("usp_ListSubmissions", (MySqlConnection)conn) { CommandType = CommandType.StoredProcedure })
            {
                cmd.Parameters.AddWithValue("pi_employee_id", employeeId);
                cmd.Parameters.AddWithValue("pi_role", role);
                cmd.Parameters.AddWithValue("pi_status", (object)status ?? DBNull.Value);
                cmd.Parameters.AddWithValue("pi_page", page);
                cmd.Parameters.AddWithValue("pi_page_size", pageSize);

                using (var reader = cmd.ExecuteReader())
                {
                    var rows = new List<SubmissionListItem>();
                    while (reader.Read())
                    {
                        rows.Add(new SubmissionListItem
                        {
                            SubmissionId = reader.GetInt64("SubmissionId"),
                            TemplateKey = reader.GetString("TemplateKey"),
                            TemplateTitle = reader.GetString("TemplateTitle"),
                            RequestorId = reader.GetInt64("RequestorId"),
                            RequestorName = reader.GetString("RequestorName"),
                            Status = reader.GetString("Status"),
                            SubmittedAt = reader.IsDBNull(reader.GetOrdinal("SubmittedAt")) ? (DateTime?)null : reader.GetDateTime("SubmittedAt"),
                            ClosedAt = reader.IsDBNull(reader.GetOrdinal("ClosedAt")) ? (DateTime?)null : reader.GetDateTime("ClosedAt"),
                            DecisionNote = reader.IsDBNull(reader.GetOrdinal("DecisionNote")) ? null : reader.GetString("DecisionNote"),
                            Version = reader.GetInt32("Version")
                        });
                    }

                    return rows;
                }
            }
        }

        /// <summary>Header + steps + events in three result sets.</summary>
        public SubmissionDetail GetSubmission(long submissionId, long employeeId, string role)
        {
            using (var conn = Open())
            using (var cmd = new MySqlCommand("usp_GetSubmission", (MySqlConnection)conn) { CommandType = CommandType.StoredProcedure })
            {
                cmd.Parameters.AddWithValue("pi_submission_id", submissionId);
                cmd.Parameters.AddWithValue("pi_employee_id", employeeId);
                cmd.Parameters.AddWithValue("pi_role", role);

                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        throw new InvalidOperationException("FORMS:SUBMISSION_NOT_FOUND");
                    }

                    var detail = new SubmissionDetail
                    {
                        SubmissionId = reader.GetInt64("SubmissionId"),
                        TemplateKey = reader.GetString("TemplateKey"),
                        TemplateTitle = reader.GetString("TemplateTitle"),
                        FieldSchema = reader.GetString("FieldSchema"),
                        RequestorId = reader.GetInt64("RequestorId"),
                        RequestorName = reader.GetString("RequestorName"),
                        Status = reader.GetString("Status"),
                        Payload = reader.GetString("Payload"),
                        SubmittedAt = reader.IsDBNull(reader.GetOrdinal("SubmittedAt")) ? (DateTime?)null : reader.GetDateTime("SubmittedAt"),
                        ClosedAt = reader.IsDBNull(reader.GetOrdinal("ClosedAt")) ? (DateTime?)null : reader.GetDateTime("ClosedAt"),
                        DecisionNote = reader.IsDBNull(reader.GetOrdinal("DecisionNote")) ? null : reader.GetString("DecisionNote"),
                        Version = reader.GetInt32("Version")
                    };

                    reader.NextResult();
                    while (reader.Read())
                    {
                        detail.Steps.Add(new StepRow
                        {
                            StepId = reader.GetInt64("StepId"),
                            StepSequence = reader.GetInt32("StepSequence"),
                            RoleRequired = reader.GetString("RoleRequired"),
                            Decision = reader.GetString("Decision"),
                            DecidedBy = reader.IsDBNull(reader.GetOrdinal("DecidedBy")) ? (long?)null : reader.GetInt64("DecidedBy"),
                            DecidedByName = reader.IsDBNull(reader.GetOrdinal("DecidedByName")) ? null : reader.GetString("DecidedByName"),
                            DecidedAt = reader.IsDBNull(reader.GetOrdinal("DecidedAt")) ? (DateTime?)null : reader.GetDateTime("DecidedAt"),
                            Note = reader.IsDBNull(reader.GetOrdinal("Note")) ? null : reader.GetString("Note")
                        });
                    }

                    reader.NextResult();
                    while (reader.Read())
                    {
                        detail.Events.Add(new EventRow
                        {
                            EventType = reader.GetString("EventType"),
                            ActorId = reader.IsDBNull(reader.GetOrdinal("ActorId")) ? (long?)null : reader.GetInt64("ActorId"),
                            Detail = reader.IsDBNull(reader.GetOrdinal("Detail")) ? null : reader.GetString("Detail"),
                            OccurredAt = reader.GetDateTime("OccurredAt")
                        });
                    }

                    return detail;
                }
            }
        }

        /// <summary>Creates (and optionally submits) a new form submission.</summary>
        public long CreateSubmission(int templateId, long requestorId, string payloadJson, bool submitNow)
        {
            using (var conn = Open())
            using (var cmd = new MySqlCommand("usp_CreateSubmission", (MySqlConnection)conn) { CommandType = CommandType.StoredProcedure })
            {
                cmd.Parameters.AddWithValue("pi_template_id", templateId);
                cmd.Parameters.AddWithValue("pi_requestor_id", requestorId);
                cmd.Parameters.AddWithValue("pi_payload", payloadJson);
                cmd.Parameters.AddWithValue("pi_submit_now", submitNow ? 1 : 0);
                cmd.Parameters.Add(new MySqlParameter("po_submission_id", MySqlDbType.Int64) { Direction = ParameterDirection.Output });

                cmd.ExecuteNonQuery();
                return Convert.ToInt64(cmd.Parameters["po_submission_id"].Value);
            }
        }

        /// <summary>Approval engine entry point; throws MySqlException carrying FORMS: codes.</summary>
        public int Transition(long submissionId, long actorId, string actorRole, string decision, string note, int seenVersion)
        {
            using (var conn = Open())
            using (var cmd = new MySqlCommand("usp_Transition", (MySqlConnection)conn) { CommandType = CommandType.StoredProcedure })
            {
                cmd.Parameters.AddWithValue("pi_submission_id", submissionId);
                cmd.Parameters.AddWithValue("pi_actor_id", actorId);
                cmd.Parameters.AddWithValue("pi_actor_role", actorRole);
                cmd.Parameters.AddWithValue("pi_decision", decision);
                cmd.Parameters.AddWithValue("pi_note", (object)note ?? DBNull.Value);
                cmd.Parameters.AddWithValue("pi_version", seenVersion);
                cmd.Parameters.Add(new MySqlParameter("po_new_version", MySqlDbType.Int32) { Direction = ParameterDirection.Output });

                cmd.ExecuteNonQuery();
                return Convert.ToInt32(cmd.Parameters["po_new_version"].Value);
            }
        }

        /// <summary>Queue counters for the dashboard widget.</summary>
        public IList<WorkloadRow> WorkloadSummary()
        {
            using (var conn = Open())
            using (var cmd = new MySqlCommand("usp_WorkloadSummary", (MySqlConnection)conn) { CommandType = CommandType.StoredProcedure })
            using (var reader = cmd.ExecuteReader())
            {
                var rows = new List<WorkloadRow>();
                while (reader.Read())
                {
                    rows.Add(new WorkloadRow
                    {
                        Status = reader.GetString("Status"),
                        RoleRequired = reader.GetString("RoleRequired"),
                        PendingSubmissions = reader.GetInt32("PendingSubmissions")
                    });
                }

                return rows;
            }
        }

        private IDbConnection Open()
        {
            var conn = new MySqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
    }
}
