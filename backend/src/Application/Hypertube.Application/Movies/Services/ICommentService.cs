using Hypertube.Application.Movies.DTOs;

namespace Hypertube.Application.Movies.Services;

public interface ICommentService
{
    Task<List<CommentDto>> GetLatestCommentsAsync(int limit = 20, CancellationToken cancellationToken = default);

    Task<List<CommentDto>> GetMovieCommentsAsync(Guid movieId, CancellationToken cancellationToken = default);

    Task<CommentDto?> GetCommentAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CommentDto> CreateCommentAsync(Guid userId, Guid movieId, string content, CancellationToken cancellationToken = default);

    Task<CommentDto?> UpdateCommentAsync(Guid commentId, Guid userId, string content, CancellationToken cancellationToken = default);

    Task<bool> DeleteCommentAsync(Guid commentId, Guid userId, CancellationToken cancellationToken = default);
}
