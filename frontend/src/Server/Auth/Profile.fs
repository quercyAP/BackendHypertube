namespace Server.Auth

open Shared
open System.Net
open FSharp.Json
open Microsoft.AspNetCore.Http
open FSharp.Control

module Profile =
    let profileRequest ctx = async {
       try
           let! response = AuthUtils.getAsync ctx "me"

           printfn $"raw {response}"

           if response.IsSuccessStatusCode then
               let! json = response.Content.ReadAsStringAsync() |> Async.AwaitTask
               let user = Json.deserialize<UserDto> json
               return user |> AuthApiResult.UserDto |> Ok
           else
               match response.StatusCode with
               | HttpStatusCode.Unauthorized ->
                   return (AuthApiError.TokenExpiredError) |> Error
               | _ ->
                   let! error = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                   return (AuthApiError.JsonDecodingError error) |> Error
       with ex ->
           return (AuthApiError.HttpError ex.Message) |> Error
   }
