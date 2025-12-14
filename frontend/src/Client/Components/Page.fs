namespace Client.Components

open Shared

type PasswordResetMode =
    | ForgotPassword
    | ResetPassword

type AuthMode =
    | Login
    | Register
    | PasswordReset of PasswordResetMode

type Page =
    | Movies
    | MoviePage of string
    | AuthPage of AuthMode
    | NotFound

type PageContext = {
    IsAuthenticated: bool
    CurrentUser: UserDto option
}
