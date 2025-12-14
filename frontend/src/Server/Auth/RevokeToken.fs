namespace Server.Auth

open Shared
open FSharp.Json
open System.Net.Http
open FSharp.Control
open System

module RevokeToken =

    let revokeTokenRequest ctx = async {
        try
            let _, refreshToken = AuthUtils.getTokensFromCookies ctx

            let request = { refreshToken = refreshToken |> Option.defaultValue "" }
            let json = Json.serialize request
            use content = new StringContent(json, Text.Encoding.UTF8, "application/json")

            let! response = AuthUtils.postAsync ctx "revoke" content

            printfn "response received"
            printfn $"Status code: {response.StatusCode} / {response.IsSuccessStatusCode}"

            AuthUtils.deleteTokens ctx
            return "Successfully logged out" |> AuthApiResult.String |> Ok
        with ex ->
            return Error (AuthApiError.HttpError ex.Message)
    }
