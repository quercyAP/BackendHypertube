using Hypertube.Domain.Entities;
using System.Security.Claims;

namespace Hypertube.Application.Authentication.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    RefreshToken GenerateRefreshToken(Guid userId, string? ipAddress);
    ClaimsPrincipal? ValidateAccessToken(string token);
    Task<RefreshToken?> ValidateRefreshTokenAsync(string token, CancellationToken cancellationToken = default);
    Task RevokeRefreshTokenAsync(RefreshToken token, string? ipAddress, string? reason, CancellationToken cancellationToken = default);
    Task RevokeAllUserRefreshTokensAsync(Guid userId, string? ipAddress, string? reason, CancellationToken cancellationToken = default);
}
