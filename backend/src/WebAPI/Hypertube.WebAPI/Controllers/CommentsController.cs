using System.Security.Claims;
using Hypertube.Application.Movies.DTOs;
using Hypertube.Application.Movies.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hypertube.WebAPI.Controllers;

/// <summary>
/// Comments management endpoints
/// </summary>
[ApiController]
[Route("api/comments")]
[Produces("application/json")]
public class CommentsController : ControllerBase
{
    private readonly ICommentService _commentService;
    private readonly ILogger<CommentsController> _logger;

    public CommentsController(
        ICommentService commentService,
        ILogger<CommentsController> logger)
    {
        _commentService = commentService;
        _logger = logger;
    }

    /// <summary>
    /// Get latest comments across all movies
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLatestComments(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit < 1 || limit > 100) limit = 20;

        var comments = await _commentService.GetLatestCommentsAsync(limit, cancellationToken);
        return Ok(comments);
    }

    /// <summary>
    /// Get specific comment by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetComment(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var comment = await _commentService.GetCommentAsync(id, cancellationToken);

        if (comment == null)
        {
            return NotFound(new { message = "Comment not found" });
        }

        return Ok(comment);
    }

    /// <summary>
    /// Update a comment (owner only)
    /// </summary>
    [HttpPatch("{id}")]
    [Authorize]
    public async Task<IActionResult> UpdateComment(
        Guid id,
        [FromBody] UpdateCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { message = "User not authenticated" });
        }

        try
        {
            var comment = await _commentService.UpdateCommentAsync(id, userId, request.Content, cancellationToken);

            if (comment == null)
            {
                return NotFound(new { message = "Comment not found" });
            }

            return Ok(comment);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Delete a comment (owner only)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteComment(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { message = "User not authenticated" });
        }

        try
        {
            var deleted = await _commentService.DeleteCommentAsync(id, userId, cancellationToken);

            if (!deleted)
            {
                return NotFound(new { message = "Comment not found" });
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Get comments for a specific movie
    /// </summary>
    [HttpGet("~/api/movies/{movieId}/comments")]
    public async Task<IActionResult> GetMovieComments(
        Guid movieId,
        CancellationToken cancellationToken = default)
    {
        var comments = await _commentService.GetMovieCommentsAsync(movieId, cancellationToken);
        return Ok(comments);
    }

    /// <summary>
    /// Create a comment on a movie (requires authentication)
    /// </summary>
    [HttpPost("~/api/movies/{movieId}/comments")]
    [Authorize]
    public async Task<IActionResult> CreateComment(
        Guid movieId,
        [FromBody] CreateCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { message = "User not authenticated" });
        }

        try
        {
            var comment = await _commentService.CreateCommentAsync(userId, movieId, request.Content, cancellationToken);
            return CreatedAtAction(nameof(GetComment), new { id = comment.Id }, comment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
