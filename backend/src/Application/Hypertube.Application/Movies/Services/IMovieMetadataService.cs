using Hypertube.Application.Movies.DTOs;

namespace Hypertube.Application.Movies.Services;

public interface IMovieMetadataService
{
    Task<MovieMetadataDto?> GetMetadataAsync(string title, int? year = null, CancellationToken cancellationToken = default);
}
