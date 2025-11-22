using Hypertube.Application.Profile.DTOs;
using Hypertube.Application.Profile.Services;
using Hypertube.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.Services;

public class ProfileService : IProfileService
{
    private readonly UserManager<User> _userManager;
    private readonly ILogger<ProfileService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _uploadPath;
    private readonly long _maxFileSizeBytes;
    private readonly string[] _allowedExtensions;
    private readonly string _baseUrl;

    public ProfileService(
        UserManager<User> userManager,
        ILogger<ProfileService> logger,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _logger = logger;
        _configuration = configuration;

        _uploadPath = configuration["FileUpload:ProfilePictures:Path"] ?? "/app/uploads/profile-pictures";
        _maxFileSizeBytes = long.Parse(configuration["FileUpload:ProfilePictures:MaxFileSizeBytes"] ?? "5242880");
        _allowedExtensions = configuration.GetSection("FileUpload:ProfilePictures:AllowedExtensions").Get<string[]>()
            ?? new[] { ".jpg", ".jpeg", ".png", ".gif" };
        _baseUrl = configuration["FileUpload:ProfilePictures:BaseUrl"] ?? "http://localhost:5000/uploads/profile-pictures";
    }

    public async Task<UserProfileDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.Users
            .Where(u => u.Id == userId)
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
            .FirstOrDefaultAsync(cancellationToken);

        return user;
    }

    public async Task<UserProfileDto?> GetProfileByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.Users
            .Where(u => u.UserName == username)
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
            .FirstOrDefaultAsync(cancellationToken);

        return user;
    }

    public async Task<(bool Success, string[] Errors)> UpdateProfileAsync(
        Guid userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, new[] { "User not found" });
        }

        var errors = new List<string>();

        // Update email if provided and different
        if (!string.IsNullOrWhiteSpace(request.Email) && request.Email != user.Email)
        {
            // Check if email is already taken
            var existingUser = await _userManager.FindByEmailAsync(request.Email);
            if (existingUser != null && existingUser.Id != userId)
            {
                errors.Add("Email is already taken");
            }
            else
            {
                user.Email = request.Email;
                user.NormalizedEmail = request.Email.ToUpperInvariant();
                // Note: In production, you should send a confirmation email
                user.EmailConfirmed = false;
            }
        }

        // Update other fields if provided
        if (request.FirstName != null)
        {
            user.FirstName = request.FirstName;
        }

        if (request.LastName != null)
        {
            user.LastName = request.LastName;
        }

        if (request.PreferredLanguage != null)
        {
            user.PreferredLanguage = request.PreferredLanguage;
        }

        if (errors.Any())
        {
            return (false, errors.ToArray());
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return (false, result.Errors.Select(e => e.Description).ToArray());
        }

        _logger.LogInformation("User {UserId} updated their profile", userId);
        return (true, Array.Empty<string>());
    }

    public async Task<(bool Success, string? ProfilePictureUrl, string? Error)> UploadProfilePictureAsync(
        Guid userId,
        Stream fileStream,
        string fileName,
        string contentType,
        long fileSize,
        CancellationToken cancellationToken = default)
    {
        // Validate file
        if (fileStream == null || fileSize == 0)
        {
            return (false, null, "No file provided");
        }

        // Validate file size
        if (fileSize > _maxFileSizeBytes)
        {
            var maxSizeMB = _maxFileSizeBytes / 1024.0 / 1024.0;
            return (false, null, $"File size exceeds maximum allowed size of {maxSizeMB:F1} MB");
        }

        // Validate file extension
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!_allowedExtensions.Contains(extension))
        {
            return (false, null, $"File type not allowed. Allowed types: {string.Join(", ", _allowedExtensions)}");
        }

        // Validate content type
        var allowedContentTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif" };
        if (!allowedContentTypes.Contains(contentType.ToLowerInvariant()))
        {
            return (false, null, "Invalid file content type");
        }

        // Get user
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, null, "User not found");
        }

        try
        {
            // Ensure upload directory exists
            if (!Directory.Exists(_uploadPath))
            {
                Directory.CreateDirectory(_uploadPath);
                _logger.LogInformation("Created upload directory: {Path}", _uploadPath);
            }

            // Delete old profile picture if exists
            if (!string.IsNullOrEmpty(user.ProfilePictureUrl))
            {
                try
                {
                    var oldFileName = Path.GetFileName(new Uri(user.ProfilePictureUrl).LocalPath);
                    var oldFilePath = Path.Combine(_uploadPath, oldFileName);
                    if (File.Exists(oldFilePath))
                    {
                        File.Delete(oldFilePath);
                        _logger.LogInformation("Deleted old profile picture: {Path}", oldFilePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete old profile picture for user {UserId}", userId);
                }
            }

            // Generate unique filename
            var uniqueFileName = $"{userId}_{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(_uploadPath, uniqueFileName);

            // Save file
            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await fileStream.CopyToAsync(stream, cancellationToken);
            }

            // Generate URL
            var profilePictureUrl = $"{_baseUrl}/{uniqueFileName}";

            // Update user
            user.ProfilePictureUrl = profilePictureUrl;
            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
            {
                // Delete uploaded file if user update fails
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                return (false, null, string.Join(", ", result.Errors.Select(e => e.Description)));
            }

            _logger.LogInformation(
                "User {UserId} uploaded profile picture: {FileName}",
                userId,
                uniqueFileName
            );

            return (true, profilePictureUrl, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading profile picture for user {UserId}", userId);
            return (false, null, "An error occurred while uploading the file");
        }
    }
}
