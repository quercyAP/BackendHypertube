namespace Shared

type MoviesApiError =
    | HttpError of  string
    | JsonDecodingError of string
    | TokenExpiredError

type MoviesApiResult =
    | MovieListResponse of MovieListResponse
    | MovieDto of MovieDto
    | MovieDetailsDto of MovieDetailsDto

type IMoviesApi = {
    listMovies: unit -> Async<Result<MovieListResponse, MoviesApiError>>

    getSortedMovies: string -> Async<Result<MovieListResponse, MoviesApiError>>

    searchMovies: string -> Async<Result<MovieListResponse, MoviesApiError>>

    getMovieById: string -> Async<Result<MovieDetailsDto, MoviesApiError>>
}
