using System;
using Microsoft.AspNetCore.Http;

namespace project_1.Models
{
    public class LeaveApplication
    {
        public int Id { get; set; }
        public string ApplicantPNo { get; set; } = string.Empty;
        public string ApplicantName { get; set; } = string.Empty;
        public string LeaveType { get; set; } = string.Empty;
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime EndDate { get; set; } = DateTime.Today;
        public int TotalDays { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string AddressOnLeave { get; set; } = string.Empty;
        public string ContactNo { get; set; } = string.Empty;
        public string CityOnLeave { get; set; } = string.Empty;
        public string ManagerPNo { get; set; } = string.Empty;
        public IFormFile? Attachment { get; set; }
        public string? DocumentPath { get; set; }
        public string Status { get; set; } = "Pending Executive Review";
        public string? AssignedToRole { get; set; }
        public string? ExecutiveRemarks { get; set; }
        public string? AdminRemarks { get; set; }
        public string? ApproverRemarks { get; set; }
        public DateTime AppliedDate { get; set; } = DateTime.Now;
    }
}