using Hypertube.Application.Authentication.DTOs;
using Hypertube.Application.Authentication.Services;
using Hypertube.Application.Common.Services;
using Hypertube.Domain.Entities;
using Hypertube.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hypertube.Infrastructure.Authentication;

public class AuthenticationService : IAuthenticationService
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly HypertubeDbContext _context;
    private readonly IEmailService _emailService;

    public AuthenticationService(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        ITokenService tokenService,
        HypertubeDbContext context,
        IEmailService emailService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _context = context;
        _emailService = emailService;
    }

    public async Task<AuthenticationResult> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        // Check if username already exists
        var existingUser = await _userManager.FindByNameAsync(request.Username);
        if (existingUser != null)
        {
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Username already exists",
                Errors = new Dictionary<string, string[]>
                {
                    { "Username", new[] { "This username is already taken" } }
                }
            };
        }

        // Check if email already exists
        existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
        {
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Email already exists",
                Errors = new Dictionary<string, string[]>
                {
                    { "Email", new[] { "This email is already registered" } }
                }
            };
        }

        // Create new user
        var user = new User
        {
            UserName = request.Username,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PreferredLanguage = request.PreferredLanguage,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Registration failed",
                Errors = result.Errors.GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray())
            };
        }

        // Generate tokens
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id, ipAddress);

        // Save refresh token
        await _context.RefreshTokens.AddAsync(refreshToken, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthenticationResult
        {
            Success = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            User = new UserDto
            {
                Id = user.Id,
                Username = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                FirstName = user.FirstName ?? string.Empty,
                LastName = user.LastName ?? string.Empty,
                AvatarUrl = user.ProfilePictureUrl,
                PreferredLanguage = user.PreferredLanguage,
                CreatedAt = user.CreatedAt
            }
        };
    }

    public async Task<AuthenticationResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        // Find user by username or email
        var user = await _userManager.FindByNameAsync(request.UsernameOrEmail)
                   ?? await _userManager.FindByEmailAsync(request.UsernameOrEmail);

        if (user == null)
        {
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Invalid username/email or password"
            };
        }

        // Check password
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            var errorMessage = result.IsLockedOut
                ? "Account is locked out"
                : result.IsNotAllowed
                    ? "Sign in not allowed"
                    : "Invalid username/email or password";

            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }

        // Generate tokens
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id, ipAddress);

        // Save refresh token
        await _context.RefreshTokens.AddAsync(refreshToken, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthenticationResult
        {
            Success = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            User = new UserDto
            {
                Id = user.Id,
                Username = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                FirstName = user.FirstName ?? string.Empty,
                LastName = user.LastName ?? string.Empty,
                AvatarUrl = user.ProfilePictureUrl,
                PreferredLanguage = user.PreferredLanguage,
                CreatedAt = user.CreatedAt
            }
        };
    }

    public async Task<AuthenticationResult> RefreshTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var token = await _tokenService.ValidateRefreshTokenAsync(refreshToken, cancellationToken);

        if (token == null)
        {
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Invalid or expired refresh token"
            };
        }

        var user = await _userManager.FindByIdAsync(token.UserId.ToString());

        if (user == null)
        {
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "User not found"
            };
        }

        // Generate new tokens
        var newAccessToken = _tokenService.GenerateAccessToken(user);
        var newRefreshToken = _tokenService.GenerateRefreshToken(user.Id, ipAddress);

        // Revoke old refresh token and save new one
        token.ReplacedByToken = newRefreshToken.Token;
        await _tokenService.RevokeRefreshTokenAsync(token, ipAddress, "Replaced by new token", cancellationToken);

        await _context.RefreshTokens.AddAsync(newRefreshToken, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthenticationResult
        {
            Success = true,
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };
    }

    public async Task<bool> RevokeTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var token = await _tokenService.ValidateRefreshTokenAsync(refreshToken, cancellationToken);

        if (token == null)
            return false;

        await _tokenService.RevokeRefreshTokenAsync(token, ipAddress, "Revoked by user", cancellationToken);
        return true;
    }

    public async Task<UserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user == null)
            return null;

        return new UserDto
        {
            Id = user.Id,
            Username = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName ?? string.Empty,
            LastName = user.LastName ?? string.Empty,
            AvatarUrl = user.ProfilePictureUrl,
            PreferredLanguage = user.PreferredLanguage,
            CreatedAt = user.CreatedAt
        };
    }

    public async Task<AuthenticationResult> ExternalLoginAsync(ExternalLoginRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        // Check if user already has this OAuth provider linked
        var oauthProvider = await _context.OAuthProviders
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Provider == request.Provider && o.ProviderUserId == request.ProviderUserId, cancellationToken);

        User user;

        if (oauthProvider != null)
        {
            // User already exists with this OAuth provider
            user = oauthProvider.User;
        }
        else
        {
            // Check if user exists by email
            user = await _userManager.FindByEmailAsync(request.Email);

            if (user == null)
            {
                // Create new user
                var username = request.Username ?? request.Email.Split('@')[0];

                // Ensure username is unique
                var baseUsername = username;
                var counter = 1;
                while (await _userManager.FindByNameAsync(username) != null)
                {
                    username = $"{baseUsername}{counter}";
                    counter++;
                }

                user = new User
                {
                    UserName = username,
                    Email = request.Email,
                    EmailConfirmed = true, // OAuth emails are pre-verified
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    ProfilePictureUrl = request.ProfilePictureUrl,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                var result = await _userManager.CreateAsync(user);

                if (!result.Succeeded)
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to create user account",
                        Errors = result.Errors.GroupBy(e => e.Code)
                            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray())
                    };
                }
            }

            // Link OAuth provider to user
            var newOAuthProvider = new OAuthProvider
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Provider = request.Provider,
                ProviderUserId = request.ProviderUserId,
                CreatedAt = DateTime.UtcNow
            };

            await _context.OAuthProviders.AddAsync(newOAuthProvider, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        // Generate tokens
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id, ipAddress);

        // Save refresh token
        await _context.RefreshTokens.AddAsync(refreshToken, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthenticationResult
        {
            Success = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            User = new UserDto
            {
                Id = user.Id,
                Username = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                FirstName = user.FirstName ?? string.Empty,
                LastName = user.LastName ?? string.Empty,
                AvatarUrl = user.ProfilePictureUrl,
                PreferredLanguage = user.PreferredLanguage,
                CreatedAt = user.CreatedAt
            }
        };
    }

    public async Task<bool> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(email);

        if (user == null)
        {
            // Return true even if user doesn't exist to prevent email enumeration
            return true;
        }

        // Generate password reset token
        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);

        // Send email with reset token
        await _emailService.SendPasswordResetEmailAsync(email, resetToken, cancellationToken);

        return true;
    }

    public async Task<(bool Success, string? ErrorMessage)> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        if (user == null)
        {
            return (false, "Invalid request");
        }

        // Reset password using the token
        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, errors);
        }

        // Revoke all refresh tokens for security
        await _tokenService.RevokeAllUserRefreshTokensAsync(user.Id, null, "Password reset", cancellationToken);

        return (true, null);
    }
}
