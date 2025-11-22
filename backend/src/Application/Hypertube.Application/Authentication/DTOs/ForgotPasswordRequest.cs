using System.ComponentModel.DataAnnotations;

namespace Hypertube.Application.Authentication.DTOs;

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;
}
