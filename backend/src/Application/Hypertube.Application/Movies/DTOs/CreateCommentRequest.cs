using System.ComponentModel.DataAnnotations;

namespace Hypertube.Application.Movies.DTOs;

public class CreateCommentRequest
{
    [Required(ErrorMessage = "Content is required")]
    [StringLength(1000, MinimumLength = 1, ErrorMessage = "Content must be between 1 and 1000 characters")]
    public string Content { get; set; } = string.Empty;
}
