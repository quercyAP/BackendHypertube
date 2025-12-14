namespace Server.Auth

open Shared
open FSharp.Json
open System.Net.Http
open System

module PasswordReset =
    let forgotPasswordRequest=
        fun ctx (req: ForgotPasswordRequest) ->
            async {
                try
                    let json = Json.serialize req
                    use content = new StringContent(json, Text.Encoding.UTF8, "application/json")
                    let! response = AuthUtils.postAsync ctx "forgot-password" content

                    printfn "response received"
                    printfn $"Status code: {response.StatusCode} / {response.IsSuccessStatusCode}"

                    let! result = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    printfn $"raw {result}"

                    if response.IsSuccessStatusCode then
                        printfn "✅ Forgot password request succeeded"
                        let message = Json.deserialize<{| message: string |}> result

                        return message.message
                        |> AuthApiResult.PasswordForgotResult
                        |> Ok

                    else
                        printfn $"❌ Forgot password request returned {response.StatusCode}"
                        let (errorResult: ValidationErrorResult) =
                            Json.deserialize<ValidationErrorResult> result
                        return Error (AuthApiError.ValidationError errorResult)

                with ex ->
                    printfn $"Error occured password forgotten: {ex.Message}"
                    return Error (AuthApiError.HttpError ex.Message)
            }

    let resetPasswordRequest =
        fun ctx (req: ResetPasswordRequest) ->
            async {
                try
                    let json = Json.serialize req
                    use content = new StringContent(json, Text.Encoding.UTF8, "application/json")
                    let! response = AuthUtils.postAsync ctx "reset-password" content

                    printfn "response received"
                    printfn $"Status code: {response.StatusCode} / {response.IsSuccessStatusCode}"

                    let! result = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    printfn $"raw {result}"

                    if response.IsSuccessStatusCode then
                        printfn "✅ Reset password request succeeded"
                        return "Password reset successfully"
                        |> AuthApiResult.PasswordResetResult
                        |> Ok
                    else
                        printfn $"❌ Reset password request returned {response.StatusCode}"
                        let (errorResult: ValidationErrorResult) =
                            Json.deserialize<ValidationErrorResult> result
                        return Error (AuthApiError.ValidationError errorResult)
                with ex ->
                    printfn $"Error occured password reset: {ex.Message}"
                    return Error (AuthApiError.HttpError ex.Message)
            }
