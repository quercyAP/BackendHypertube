namespace Server.Auth

open System.Net
open FSharp.Control

module CheckAuth =

    let checkIfAuthorized ctx = async {
        let accessToken, _ = AuthUtils.getTokensFromCookies ctx
        AuthUtils.setTokens ctx accessToken None None None
        let! response = AuthUtils.getAsync ctx "me"
        if response.StatusCode = HttpStatusCode.Unauthorized then
            return false
        else
            return true
        }

    let apiCallWithAuthCheck ctx
        call : Async<'T> = async {
        let! isAuthorized = checkIfAuthorized ctx

        printfn "api call with auth check"

        if not isAuthorized then
            match! (RefreshToken.refreshToken ctx) with
            | Error _ ->
                printfn " refresh error"

            | Ok _ ->
                printfn " refresh ok"

        return! call
    }
