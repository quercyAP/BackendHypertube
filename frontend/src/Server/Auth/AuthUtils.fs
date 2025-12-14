namespace Server.Auth

open System
open FSharp.Json
open Microsoft.AspNetCore.Http
open SAFE
open Shared
open System.Net.Http
open FSharp.Control
open Configuration

module AuthUtils =
    let postAsync (ctx: HttpContext) =
        ApiUtils.postAsync (getConfiguration ctx.Configuration).BackendAuthUrl

    let getAsync (ctx: HttpContext) =
        ApiUtils.getAsync (getConfiguration ctx.Configuration).BackendAuthUrl

    let getAsyncWithParams (ctx: HttpContext) =
        ApiUtils.getAsyncWithParams (getConfiguration ctx.Configuration).BackendAuthUrl

    let postAsyncWithAuth (ctx: HttpContext) =
        ApiUtils.postAsync (getConfiguration ctx.Configuration).BackendAuthUrl

    let setTokens (ctx: HttpContext) =
        ApiUtils.setTokens ctx

    let deleteTokens (ctx: HttpContext) = ApiUtils.deleteTokens ctx

    let getTokensFromCookies  (ctx: HttpContext)=
        ApiUtils.getTokensFromCookies ctx

    let parseAuthenticationResult (ctx: HttpContext) (json: string) =
        let (authResult: AuthenticationResult) =
            Json.deserialize<AuthenticationResult> json

        printfn $"✅ authresult deserialized: {authResult}"
        setTokens ctx authResult.accessToken authResult.refreshToken authResult.expiresAt authResult.refreshExpiresAt

        { authResult with
            accessToken = Some "obfuscated"
            refreshToken = Some "obfuscated"
        }
