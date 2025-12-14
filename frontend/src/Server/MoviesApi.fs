module MoviesApi

open Microsoft.AspNetCore.Http
open Shared

open Server.Movies

let moviesApi (ctx: HttpContext) : IMoviesApi = {
    listMovies = fun _ -> Movies.getPopularMoviesRequest ctx

    getSortedMovies = Movies.getSortedMovies ctx

    searchMovies = Movies.searchMovies ctx

    getMovieById = Movies.getMovieById ctx
}
