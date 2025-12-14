module AuthApi

open System
open System.Net
open FSharp.Json
open Microsoft.AspNetCore.Http
open Shared
open System.Net.Http
open FSharp.Control
// open System.Text.Json

open Configuration
open Microsoft.Extensions.Configuration
open Giraffe

open Server.Auth

let authApi (ctx: HttpContext) : IAuthApi =
    {
    register = Register.registerRequest ctx

    login = Login.loginRequest ctx

    refresh = fun _ -> RefreshToken.refreshTokenRequest ctx

    revoke = fun _ -> RevokeToken.revokeTokenRequest ctx

    profile = fun _ -> CheckAuth.apiCallWithAuthCheck ctx (Profile.profileRequest ctx)

    forgotPassword = PasswordReset.forgotPasswordRequest ctx

    resetPassword = PasswordReset.resetPasswordRequest ctx
}
