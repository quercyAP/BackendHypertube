namespace Shared

type ApiError = {
    errorType: string
    title: string
    status: int32
    detail: string
    instance: string
}

type ValidationErrorResult = {
    ``type``: string option
    title: string option
    status: int32 option
    errors: Map<string, string list> option
    traceId: string option
    instance: string option
}

type LoginApiError = {
    message: string option
}

type AuthApiError =
    | ApiError of ApiError
    | ValidationError of ValidationErrorResult
    | JsonDecodingError of string
    | HttpError of string
    | LoginApiError of LoginApiError
    | TokenExpiredError

type AuthApiResult =
    | AuthenticationResult of AuthenticationResult
    | UserDto of UserDto
    | String of string
    | PasswordForgotResult of string
    | PasswordResetResult of string

type IAuthApi = {
    register: RegisterRequest -> Async<Result<AuthApiResult, AuthApiError>>
    login: LoginRequest -> Async<Result<AuthApiResult, AuthApiError>>
    refresh: unit -> Async<Result<AuthApiResult, AuthApiError>>
    revoke: unit -> Async<Result<AuthApiResult, AuthApiError>>
    // externalLogin: ExternalLoginRequest -> Async<Result<AuthenticationResult, UserApiError>>
    forgotPassword: ForgotPasswordRequest -> Async<Result<AuthApiResult, AuthApiError>>
    resetPassword: ResetPasswordRequest -> Async<Result<AuthApiResult, AuthApiError>>
    profile: unit -> Async<Result<AuthApiResult, AuthApiError>>
}
