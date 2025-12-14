namespace Server.Auth

open System
open System.Net
open FSharp.Json
open Shared
open System.Net.Http
open FSharp.Control

module Login =

    let loginRequest ctx req = async {
        try
            let json = Json.serialize req
            use content = new StringContent(json, Text.Encoding.UTF8, "application/json")

            let! response = AuthUtils.postAsync ctx "login" content
            printfn "response received"
            printfn $"Status code: {response.StatusCode} / {response.IsSuccessStatusCode}"

            let! result = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            printfn $"raw {result}"

            if response.IsSuccessStatusCode then
                printfn $"✅ Registration succeeded"
                let authResult = AuthUtils.parseAuthenticationResult ctx result
                return
                    authResult
                    |> AuthApiResult.AuthenticationResult
                    |> Ok
            else
                printfn $"❌ Registration returned {response.StatusCode}"
                match response.StatusCode with
                | HttpStatusCode.BadRequest ->
                    let errorResult =
                        Json.deserialize<ValidationErrorResult> result
                    return Error (AuthApiError.ValidationError errorResult)
                | HttpStatusCode.Unauthorized ->
                    let errorResult =
                        Json.deserialize<LoginApiError> result
                    return Error (AuthApiError.LoginApiError errorResult)
                | _ ->
                    let errorResult =
                        Json.deserialize<ValidationErrorResult> result
                    return Error (AuthApiError.ValidationError errorResult)

        with ex ->
            return (AuthApiError.ApiError {
                errorType = "HttpError"
                title = "HTTP error during registration"
                status = int HttpStatusCode.InternalServerError
                detail = ex.Message
                instance = ""
                })
                   |> Error
    }
