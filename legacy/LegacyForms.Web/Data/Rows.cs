using System;
using System.Collections.Generic;

namespace LegacyForms.Web.Data
{
    public class TemplateRow
    {
        public int TemplateId { get; set; }
        public string TemplateKey { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string FieldSchema { get; set; }
    }

    public class SubmissionListItem
    {
        public long SubmissionId { get; set; }
        public string TemplateKey { get; set; }
        public string TemplateTitle { get; set; }
        public long RequestorId { get; set; }
        public string RequestorName { get; set; }
        public string Status { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string DecisionNote { get; set; }
        public int Version { get; set; }
    }

    public class SubmissionDetail
    {
        public SubmissionDetail()
        {
            Steps = new List<StepRow>();
            Events = new List<EventRow>();
        }

        public long SubmissionId { get; set; }
        public string TemplateKey { get; set; }
        public string TemplateTitle { get; set; }
        public string FieldSchema { get; set; }
        public long RequestorId { get; set; }
        public string RequestorName { get; set; }
        public string Status { get; set; }
        public string Payload { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string DecisionNote { get; set; }
        public int Version { get; set; }

        public IList<StepRow> Steps { get; private set; }
        public IList<EventRow> Events { get; private set; }
    }

    public class StepRow
    {
        public long StepId { get; set; }
        public int StepSequence { get; set; }
        public string RoleRequired { get; set; }
        public string Decision { get; set; }
        public long? DecidedBy { get; set; }
        public string DecidedByName { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string Note { get; set; }
    }

    public class EventRow
    {
        public string EventType { get; set; }
        public long? ActorId { get; set; }
        public string Detail { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    public class WorkloadRow
    {
        public string Status { get; set; }
        public string RoleRequired { get; set; }
        public int PendingSubmissions { get; set; }
    }
}
