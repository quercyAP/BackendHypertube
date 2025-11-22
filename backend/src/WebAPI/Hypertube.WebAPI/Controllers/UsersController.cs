using Hypertube.Application.Profile.DTOs;
using Hypertube.Application.Profile.Services;
using Hypertube.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Hypertube.WebAPI.Controllers;

/// <summary>
/// User profile management endpoints
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IProfileService _profileService;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        IProfileService profileService,
        UserManager<User> userManager,
        ILogger<UsersController> logger)
    {
        _profileService = profileService;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Get list of users (paginated)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _userManager.Users.AsQueryable();

        // Search by username or email
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u =>
                u.UserName!.Contains(search) ||
                u.Email!.Contains(search) ||
                (u.FirstName != null && u.FirstName.Contains(search)) ||
                (u.LastName != null && u.LastName.Contains(search))
            );
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserProfileDto
            {
                Id = u.Id,
                Username = u.UserName ?? string.Empty,
                Email = u.Email ?? string.Empty,
                FirstName = u.FirstName,
                LastName = u.LastName,
                ProfilePictureUrl = u.ProfilePictureUrl,
                PreferredLanguage = u.PreferredLanguage,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            users,
            pagination = new
            {
                page,
                pageSize,
                totalCount,
                totalPages
            }
        });
    }

    /// <summary>
    /// Get user profile by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetUserById(Guid id, CancellationToken cancellationToken)
    {
        var profile = await _profileService.GetProfileAsync(id, cancellationToken);

        if (profile == null)
        {
            return NotFound(new { message = "User not found" });
        }

        return Ok(profile);
    }

    /// <summary>
    /// Get user profile by username
    /// </summary>
    [HttpGet("username/{username}")]
    public async Task<IActionResult> GetUserByUsername(string username, CancellationToken cancellationToken)
    {
        var profile = await _profileService.GetProfileByUsernameAsync(username, cancellationToken);

        if (profile == null)
        {
            return NotFound(new { message = "User not found" });
        }

        return Ok(profile);
    }

    /// <summary>
    /// Update user profile (owner only)
    /// </summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> UpdateProfile(
        Guid id,
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        // Get current user ID from JWT claims
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId) || !Guid.TryParse(currentUserId, out var userId))
        {
            return Unauthorized(new { message = "Invalid token" });
        }

        // Check if user is updating their own profile
        if (userId != id)
        {
            return Forbid();
        }

        var (success, errors) = await _profileService.UpdateProfileAsync(id, request, cancellationToken);

        if (!success)
        {
            return BadRequest(new { message = "Failed to update profile", errors });
        }

        // Return updated profile
        var updatedProfile = await _profileService.GetProfileAsync(id, cancellationToken);
        return Ok(updatedProfile);
    }

    /// <summary>
    /// Upload profile picture (owner only)
    /// </summary>
    [HttpPost("{id:guid}/profile-picture")]
    [Consumes("multipart/form-data")]
    [ApiExplorerSettings(IgnoreApi = true)] // Exclude from Swagger due to file upload complexity
    public async Task<IActionResult> UploadProfilePicture(
        Guid id,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        // Get current user ID from JWT claims
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId) || !Guid.TryParse(currentUserId, out var userId))
        {
            return Unauthorized(new { message = "Invalid token" });
        }

        // Check if user is uploading to their own profile
        if (userId != id)
        {
            return Forbid();
        }

        // Open file stream and pass to service
        await using var stream = file.OpenReadStream();
        var (success, profilePictureUrl, error) = await _profileService.UploadProfilePictureAsync(
            id,
            stream,
            file.FileName,
            file.ContentType,
            file.Length,
            cancellationToken
        );

        if (!success)
        {
            return BadRequest(new { message = error });
        }

        return Ok(new
        {
            message = "Profile picture uploaded successfully",
            profilePictureUrl
        });
    }

    /// <summary>
    /// Delete profile picture (owner only)
    /// </summary>
    [HttpDelete("{id:guid}/profile-picture")]
    public async Task<IActionResult> DeleteProfilePicture(Guid id, CancellationToken cancellationToken)
    {
        // Get current user ID from JWT claims
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId) || !Guid.TryParse(currentUserId, out var userId))
        {
            return Unauthorized(new { message = "Invalid token" });
        }

        // Check if user is deleting their own profile picture
        if (userId != id)
        {
            return Forbid();
        }

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound(new { message = "User not found" });
        }

        if (string.IsNullOrEmpty(user.ProfilePictureUrl))
        {
            return BadRequest(new { message = "No profile picture to delete" });
        }

        user.ProfilePictureUrl = null;
        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                message = "Failed to delete profile picture",
                errors = result.Errors.Select(e => e.Description)
            });
        }

        return Ok(new { message = "Profile picture deleted successfully" });
    }
}
