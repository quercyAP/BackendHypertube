namespace Server.Auth

open System
open System.Net
open FSharp.Json
open Microsoft.AspNetCore.Http
open Shared
open System.Net.Http
open FSharp.Control

module RefreshToken =
    let refreshToken (ctx:HttpContext) = async {
        try
            let _, refreshToken = AuthUtils.getTokensFromCookies ctx

            let request = { refreshToken = refreshToken |> Option.defaultValue "" }
            let json = Json.serialize request
            use content = new StringContent(json, Text.Encoding.UTF8, "application/json")
            let! response = AuthUtils.postAsync ctx "refresh" content
            printfn "response received"
            printfn $"Status code: {response.StatusCode} / {response.IsSuccessStatusCode}"
            let! result = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            printfn $"raw {result}"
            if response.IsSuccessStatusCode then
                printfn $"✅ Token refresh succeeded"
                let authResult = AuthUtils.parseAuthenticationResult ctx result
                return authResult |> Ok
            else
                printfn $"❌ Token refresh returned {response.StatusCode}"
                let (errorResult: ValidationErrorResult) =
                    Json.deserialize<ValidationErrorResult> result
                return Error (AuthApiError.ValidationError errorResult)
        with ex ->
            return Error (AuthApiError.HttpError ex.Message)
    }

    let refreshTokenRequest ctx = async {
        let! result = (refreshToken ctx)
        return result
            |> Result.map AuthApiResult.AuthenticationResult
    }
