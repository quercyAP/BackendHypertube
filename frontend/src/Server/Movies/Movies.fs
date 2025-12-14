namespace Server.Movies

open SAFE
open Shared
open System.Net
open FSharp.Json
open Microsoft.AspNetCore.Http
open FSharp.Control
open Configuration
open System.Net.Http
open Shared

module Movies =

    let getPopularMoviesRequest (ctx: HttpContext) = async {
        let parameters = Some (Map [| ("pageSize", "99") |])

        let! movies = MoviesUtils.performGetRequest<MovieListResponse> ctx "" parameters

        printfn $"raw {movies}"

        return movies
    }

    let getSortedMovies (ctx: HttpContext) sortType = async {
        let parameters = Some (Map [| ("sortBy", sortType) |])

        let! movies = MoviesUtils.performGetRequest<MovieListResponse> ctx "" parameters

        printfn $"raw {movies}"

        return movies
    }

    let searchMovies (ctx: HttpContext) searchString = async {
        let parameters = Some (Map [| ("q", searchString) |])

        let! movies = MoviesUtils.performGetRequest<MovieListResponse> ctx "search" parameters

        printfn $"raw {movies}"

        return movies
    }

    let getMovieById (ctx: HttpContext) movieId = async {
        let! movie = MoviesUtils.performGetRequest<MovieDetailsDto> ctx $"{movieId}" None

        printfn $"raw {movie}"

        return movie
    }
