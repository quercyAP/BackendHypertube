using Microsoft.AspNetCore.Identity;

namespace Hypertube.Domain.Entities;

public class User : IdentityUser<Guid>
{
    // Custom properties
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<OAuthProvider> OAuthProviders { get; set; } = new List<OAuthProvider>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
