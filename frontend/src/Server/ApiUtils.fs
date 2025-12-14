module ApiUtils

open System
open System.Net
open FSharp.Json
open Microsoft.AspNetCore.Http
open Configuration
open SAFE
open Shared
open System.Net.Http
open FSharp.Control

let client = new HttpClient()

let postAsync (baseUrl: string) =
    fun (url: string) (content: HttpContent) ->
        printfn $"Posting to {baseUrl}/{url}"
        client.PostAsync($"{baseUrl}/{url}", content) |> Async.AwaitTask

let getAsync (baseUrl: string) =
    fun (url: string) ->
        printfn $"Getting from {baseUrl}/{url}"
        client.GetAsync($"{baseUrl}/{url}") |> Async.AwaitTask

let getAsyncWithParams (baseUrl: string) =
    fun (url: string) (parameters: Map<string, string>) ->
        let parametersString =
            ("?", parameters)
            ||> Map.fold (fun state key value ->
                $"{state}&{key}={value}"
            )

        let uri =
            $"{baseUrl}/{url}{parametersString}"

        printfn $"Getting from {uri}"
        client.GetAsync($"{uri}") |> Async.AwaitTask

let setTokens (ctx: HttpContext) (accessToken: string option) (refreshToken: string option) expiresAt refreshExpiresAt=
    client.DefaultRequestHeaders.Authorization <-
        match accessToken with
        | Some token -> System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token)
        | None -> null

    //todo expiresat for refresh when implemented in backend
    let maxAgeRefresh =
        match refreshExpiresAt with
        | Some refreshExpiresAt -> refreshExpiresAt - DateTime.UtcNow
        | None -> TimeSpan.FromDays(7)
        // | _ -> TimeSpan.FromDays(7)

    let optionsRefresh = CookieOptions()
    optionsRefresh.HttpOnly <- true
    optionsRefresh.Secure <- true
    optionsRefresh.SameSite <- SameSiteMode.None
    optionsRefresh.Path <- "/"
    optionsRefresh.MaxAge <- Nullable maxAgeRefresh

    let maxAgeAccess =
        match expiresAt with
        | Some expiresAt -> expiresAt - DateTime.UtcNow
        | None -> TimeSpan.FromSeconds(5)
        // | _ -> TimeSpan.FromSeconds(5)

    let optionsAccess = CookieOptions()
    optionsAccess.HttpOnly <- true
    optionsAccess.Secure <- true
    optionsAccess.SameSite <- SameSiteMode.None
    optionsAccess.Path <- "/"
    optionsAccess.MaxAge <- Nullable maxAgeAccess

    match accessToken, refreshToken with
    | Some token, Some refresh ->
        printfn "setting access and refresh"
        ctx.Response.Cookies.Append("access_token", token, optionsAccess)
        ctx.Response.Cookies.Append("refresh_token", refresh, optionsRefresh)
    | Some token, None ->
        printfn "setting access"
        ctx.Response.Cookies.Append("access_token", token, optionsAccess)
    | None, Some refresh ->
        printfn "setting refresh"
        ctx.Response.Cookies.Append("refresh_token", refresh, optionsRefresh)
    | _ -> printfn "⚠️ No tokens in registration response."

let deleteTokens (ctx: HttpContext) =
    ctx.Response.Cookies.Delete("access_token")
    ctx.Response.Cookies.Delete("refresh_token")
    client.DefaultRequestHeaders.Authorization <- System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", null)

let getTokensFromCookies (ctx: HttpContext) =
    let accessToken =
        match ctx.Request.Cookies.TryGetValue("access_token") with
        | true, token -> Some token
        | _ -> None
    let refreshToken =
        match ctx.Request.Cookies.TryGetValue("refresh_token") with
        | true, token -> Some token
        | _ -> None
    accessToken, refreshToken
