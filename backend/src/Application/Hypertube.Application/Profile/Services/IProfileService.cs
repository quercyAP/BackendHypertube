using Hypertube.Application.Profile.DTOs;

namespace Hypertube.Application.Profile.Services;

public interface IProfileService
{
    Task<UserProfileDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserProfileDto?> GetProfileByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<(bool Success, string[] Errors)> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
    Task<(bool Success, string? ProfilePictureUrl, string? Error)> UploadProfilePictureAsync(Guid userId, Stream fileStream, string fileName, string contentType, long fileSize, CancellationToken cancellationToken = default);
}
