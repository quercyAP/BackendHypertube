namespace Server.Movies

open System
open FSharp.Json
open Microsoft.AspNetCore.Http
open SAFE
open Shared
open System.Net
open FSharp.Control
open Configuration

module MoviesUtils =
    let getAsync (ctx: HttpContext) =
        ApiUtils.getAsync (getConfiguration ctx.Configuration).BackendMoviesUrl

    let getAsyncWithParams (ctx: HttpContext) =
        ApiUtils.getAsyncWithParams (getConfiguration ctx.Configuration).BackendMoviesUrl

    let getAsyncWithAuth (ctx: HttpContext)=
        fun url ->
            Server.Auth.CheckAuth.apiCallWithAuthCheck
                ctx
                (getAsync ctx url)

    let getAsyncWithAuthParams (ctx: HttpContext) =
        fun url parameters ->
            match parameters with
            | Some args ->
                Server.Auth.CheckAuth.apiCallWithAuthCheck
                    ctx
                    (getAsyncWithParams ctx url args)
            | None ->
                Server.Auth.CheckAuth.apiCallWithAuthCheck
                    ctx
                    (getAsync ctx url)

    let performGetRequest<'T> =
        fun ctx url parameters -> async {
            try
                let! response = getAsyncWithAuthParams ctx url parameters

                printfn $"rawPERFORMGET MOVIES {response}"

                if response.IsSuccessStatusCode then
                    let! json = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    let user = Json.deserialize<'T> json
                    return user |> Ok
                else
                    match response.StatusCode with
                    | HttpStatusCode.Unauthorized ->
                        return (MoviesApiError.TokenExpiredError) |> Error
                    | _ ->
                        let! error = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                        return (MoviesApiError.JsonDecodingError error) |> Error
            with ex ->
               return (MoviesApiError.HttpError ex.Message) |> Error
        }
