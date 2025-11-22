using System.ComponentModel.DataAnnotations;

namespace Hypertube.Application.Profile.DTOs;

public class UpdateProfileRequest
{
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string? Email { get; set; }

    [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters")]
    public string? FirstName { get; set; }

    [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters")]
    public string? LastName { get; set; }

    [RegularExpression("^(en|fr)$", ErrorMessage = "Language must be 'en' or 'fr'")]
    public string? PreferredLanguage { get; set; }
}
