using System.ComponentModel.DataAnnotations;

namespace project_1.Models // Make sure this matches your project name
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Please enter your P:No")]
        [Display(Name = "P:No")]
        public string PNo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your password")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}
