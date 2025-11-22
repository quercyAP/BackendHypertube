using System.ComponentModel.DataAnnotations;

namespace Hypertube.Application.Authentication.DTOs;

public class ExternalLoginRequest
{
    [Required(ErrorMessage = "Provider is required")]
    public string Provider { get; set; } = string.Empty;

    [Required(ErrorMessage = "Provider user ID is required")]
    public string ProviderUserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;

    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ProfilePictureUrl { get; set; }
}
