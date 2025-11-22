using Hypertube.Application.Movies.DTOs;
using Hypertube.Application.Movies.Services;
using Hypertube.Domain.Entities;
using Hypertube.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hypertube.Infrastructure.Services;

public class CommentService : ICommentService
{
    private readonly HypertubeDbContext _context;
    private readonly ILogger<CommentService> _logger;

    public CommentService(
        HypertubeDbContext context,
        ILogger<CommentService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<CommentDto>> GetLatestCommentsAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting latest {Limit} comments", limit);

        var comments = await _context.Comments
            .Include(c => c.User)
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return comments.Select(MapToDto).ToList();
    }

    public async Task<List<CommentDto>> GetMovieCommentsAsync(Guid movieId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting comments for movie {MovieId}", movieId);

        var comments = await _context.Comments
            .Include(c => c.User)
            .Where(c => c.MovieId == movieId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        return comments.Select(MapToDto).ToList();
    }

    public async Task<CommentDto?> GetCommentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting comment {CommentId}", id);

        var comment = await _context.Comments
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        return comment != null ? MapToDto(comment) : null;
    }

    public async Task<CommentDto> CreateCommentAsync(
        Guid userId,
        Guid movieId,
        string content,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating comment for user {UserId} on movie {MovieId}", userId, movieId);

        // Verify movie exists
        var movieExists = await _context.Movies.AnyAsync(m => m.Id == movieId, cancellationToken);
        if (!movieExists)
        {
            throw new InvalidOperationException($"Movie with ID {movieId} not found");
        }

        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MovieId = movieId,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync(cancellationToken);

        // Reload with User navigation property
        await _context.Entry(comment).Reference(c => c.User).LoadAsync(cancellationToken);

        _logger.LogInformation("Comment {CommentId} created successfully", comment.Id);

        return MapToDto(comment);
    }

    public async Task<CommentDto?> UpdateCommentAsync(
        Guid commentId,
        Guid userId,
        string content,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating comment {CommentId} by user {UserId}", commentId, userId);

        var comment = await _context.Comments
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken);

        if (comment == null)
        {
            _logger.LogWarning("Comment {CommentId} not found", commentId);
            return null;
        }

        // Verify ownership
        if (comment.UserId != userId)
        {
            _logger.LogWarning("User {UserId} attempted to update comment {CommentId} owned by {OwnerId}",
                userId, commentId, comment.UserId);
            throw new UnauthorizedAccessException("You can only update your own comments");
        }

        comment.Content = content;
        comment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Comment {CommentId} updated successfully", commentId);

        return MapToDto(comment);
    }

    public async Task<bool> DeleteCommentAsync(
        Guid commentId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting comment {CommentId} by user {UserId}", commentId, userId);

        var comment = await _context.Comments
            .FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken);

        if (comment == null)
        {
            _logger.LogWarning("Comment {CommentId} not found", commentId);
            return false;
        }

        // Verify ownership
        if (comment.UserId != userId)
        {
            _logger.LogWarning("User {UserId} attempted to delete comment {CommentId} owned by {OwnerId}",
                userId, commentId, comment.UserId);
            throw new UnauthorizedAccessException("You can only delete your own comments");
        }

        _context.Comments.Remove(comment);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Comment {CommentId} deleted successfully", commentId);

        return true;
    }

    private CommentDto MapToDto(Comment comment)
    {
        return new CommentDto
        {
            Id = comment.Id,
            Content = comment.Content,
            Username = comment.User?.UserName ?? "Unknown",
            UserId = comment.UserId,
            MovieId = comment.MovieId,
            CreatedAt = comment.CreatedAt,
            UpdatedAt = comment.UpdatedAt
        };
    }
}
