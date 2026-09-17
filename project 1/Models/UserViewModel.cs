namespace project_1.Models
{
    public class UserViewModel
    {
        public string PNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;

        // Leave Quotas
        public int CasualQuota { get; set; } = 5;
        public int MedicalQuota { get; set; } = 5;
        public int AnnualQuota { get; set; } = 5;
        public int EmergencyQuota { get; set; } = 5;
    }
}