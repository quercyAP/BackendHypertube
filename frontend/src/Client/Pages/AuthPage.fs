module Client.Pages.AuthPage

open Client.Components
open Client.Components.Common
open Elmish
open SAFE
open Shared

let userApi = Api.makeProxy<IAuthApi> ()

type FieldType =
    | Username
    | FirstName
    | LastName
    | Email
    | Password
    | ConfirmPassword
    | ResetToken

[<RequireQualifiedAccess>]
type FieldContentType =
    | Text
    | Password

module FieldContentType =
    let toString (contentType: FieldContentType) =
        match contentType with
        | FieldContentType.Text -> "text"
        | FieldContentType.Password -> "password"

type FieldState =
    | Valid
    | Invalid of string

module FieldState =
    let toBool (state: FieldState) =
        match state with
        | Valid -> true
        | Invalid _ -> false

type Field = {
    FieldType: FieldType
    State: FieldState
    Content: string
    ExpectedType: FieldContentType
}

module Field =
    let toString (fieldType: FieldType) =
        match fieldType with
        | Username -> "Username"
        | FirstName -> "First Name"
        | LastName -> "Last Name"
        | Email -> "Email"
        | Password -> "Password"
        | ConfirmPassword -> "Confirm Password"
        | ResetToken -> "Reset Token"

type AuthForm = {
    Username: Field
    FirstName: Field
    LastName: Field
    Email: Field
    Password: Field
    ConfirmPassword: Field
    ResetToken: Field
}

module AuthForm =
    let toLoginRequest (form: AuthForm) =
        {
            usernameOrEmail = form.Username.Content
            password = form.Password.Content
            rememberMe = true
        }
        |> AuthRequest.LoginRequest

    let toRegisterRequest (form: AuthForm) =
        {
            username = form.Username.Content
            firstName = form.FirstName.Content
            lastName = form.LastName.Content
            email = form.Email.Content
            password = form.Password.Content
            confirmPassword = form.ConfirmPassword.Content
            preferredLanguage = "en"
        }
        |> AuthRequest.RegisterRequest

    let toForgotPasswordRequest (form: AuthForm) =
        {
            email = form.Email.Content
        }
        |> AuthRequest.ForgotPasswordRequest

    let toResetPasswordRequest (form: AuthForm) =
        {
            email = form.Email.Content
            token = form.ResetToken.Content
            newPassword = form.Password.Content
            confirmPassword = form.ConfirmPassword.Content
        }
        |> AuthRequest.ResetPasswordRequest

type Model = {
    CurrentMode: AuthMode
    CurrentForm: AuthForm
}

type Msg =
    | ChangeMode of AuthMode
    | AuthUser of ApiCall<AuthRequest, Result<AuthApiResult, AuthApiError>>
    | UpdateForm of FieldType * string
    | ResetForm
    | UnknownError of exn

type Intent =
    | UserAuthed
    | DoNothing

let private defaultForm =
    {
        Username = { FieldType = Username; State = Valid; Content = ""; ExpectedType = FieldContentType.Text }
        FirstName = { FieldType = FirstName; State = Valid; Content = ""; ExpectedType = FieldContentType.Text }
        LastName = { FieldType = LastName; State = Valid; Content = ""; ExpectedType = FieldContentType.Text }
        Email = { FieldType = Email; State = Valid; Content = ""; ExpectedType = FieldContentType.Text }
        Password = { FieldType = Password; State = Valid; Content = ""; ExpectedType = FieldContentType.Password }
        ConfirmPassword = { FieldType = ConfirmPassword; State = Valid; Content = ""; ExpectedType = FieldContentType.Password }
        ResetToken = { FieldType = ResetToken; State = Valid; Content = ""; ExpectedType = FieldContentType.Text }
    }

let init authMode =
    let initialModel = {
        CurrentMode = authMode
        CurrentForm = defaultForm
    }
    let initialCmd = Cmd.none

    initialModel, initialCmd

let update msg model =
    match msg with

    | AuthUser apiCall ->
        match apiCall with
        | Start request ->
            printfn $"auth request : {request}"
            let authCmd =
                match request with
                | AuthRequest.RegisterRequest registerRequest ->
                    Cmd.OfAsync.either
                        userApi.register
                        registerRequest
                        (Finished >> AuthUser)
                        UnknownError
                | AuthRequest.LoginRequest loginRequest ->
                    Cmd.OfAsync.either
                        userApi.login
                        loginRequest
                        (Finished >> AuthUser)
                        UnknownError
                | AuthRequest.ForgotPasswordRequest forgotPasswordRequest ->
                    Cmd.batch [
                        Cmd.OfAsync.either
                            userApi.forgotPassword
                            forgotPasswordRequest
                            (Finished >> AuthUser)
                            UnknownError
                        PasswordResetMode.ResetPassword |> PasswordReset |> ChangeMode |> Cmd.ofMsg
                        ]
                | AuthRequest.ResetPasswordRequest resetPasswordRequest ->
                    Cmd.OfAsync.either
                        userApi.resetPassword
                        resetPasswordRequest
                        (Finished >> AuthUser)
                        UnknownError
                | AuthRequest.ExternalLoginRequest _ ->
                    Cmd.none // External login not implemented

            model,
            Cmd.batch [
                authCmd
            ],
            Intent.DoNothing

        | Finished result ->
            printfn $"Authentication result: {result}"

            let newForm, cmd, intent =
                match result with
                | Ok (AuthApiResult.String isAuth) ->
                    printfn $"Authentication successful: {isAuth}"
                    defaultForm, Cmd.none, Intent.UserAuthed
                | Ok (AuthApiResult.UserDto user) ->
                    printfn $"User authenticated: {user}"
                    defaultForm, Cmd.none, Intent.UserAuthed
                | Ok (AuthApiResult.AuthenticationResult isAuth) ->
                    printfn $"Authentication successful: {isAuth}"
                    defaultForm, Cmd.none, Intent.UserAuthed
                | Ok (AuthApiResult.PasswordForgotResult message) ->
                    printfn $"Password forgot result: {message}"
                    model.CurrentForm, Cmd.none, Intent.DoNothing
                | Ok (AuthApiResult.PasswordResetResult message) ->
                    printfn $"Password reset result: {message}"
                    defaultForm, Login |> ChangeMode |> Cmd.ofMsg, Intent.DoNothing
                | Error err ->
                    printfn $"Authentication error: {err}"
                    match err with
                    | AuthApiError.ValidationError error ->
                        printfn $"Validation error: {error}"
                        let errorsList =
                            error.errors
                            |> Option.defaultValue Map.empty
                            |> Map.toList

                        let getErrorMsg apiError =
                            apiError |> snd |> List.tryHead |> Option.defaultValue "Invalid value"

                        let errorToState =
                            getErrorMsg >> Invalid

                        (model.CurrentForm, errorsList)
                        ||> List.fold (fun currentForm apiError ->
                            match (apiError |> fst) with
                            | "Username"
                            | "UsernameOrEmail" -> { currentForm with Username.State = apiError |> errorToState }
                            | "Firstname" -> { currentForm with FirstName.State = apiError |> errorToState }
                            | "Lastname" -> { currentForm with LastName.State = apiError |> errorToState }
                            | "Email" -> { currentForm with Email.State = apiError |> errorToState }
                            | "Password"
                            | "NewPassword"
                            | "PasswordRequiresDigit" -> { currentForm with Password.State = apiError |> errorToState }
                            | "ConfirmPassword" -> { currentForm with ConfirmPassword.State = apiError |> errorToState }
                            | "Token" -> { currentForm with ResetToken.State = apiError |> errorToState }
                            | _ -> currentForm
                        )
                        ,Cmd.none
                        , Intent.DoNothing
                    | AuthApiError.LoginApiError error ->
                        let errorMessage =
                            error.message
                            |> Option.defaultValue "Login failed due to unknown error."

                        let updatedForm =
                            { model.CurrentForm with
                                Username = { model.CurrentForm.Username with State = Invalid errorMessage }
                                Password = { model.CurrentForm.Password with State = Invalid errorMessage }
                            }

                        updatedForm, Cmd.none, Intent.DoNothing
                    | _ ->
                        printfn "An unexpected error occurred during authentication."
                        model.CurrentForm, Cmd.none, Intent.DoNothing

            { model with CurrentForm = newForm }, cmd, intent

    | UpdateForm (field, value) ->
        let updatedForm =
            match field with
            | Username ->
                let usernameField = { model.CurrentForm.Username with Content = value; State = Valid }
                { model.CurrentForm with Username = usernameField }
            | FirstName ->
                let firstNameField = { model.CurrentForm.FirstName with Content = value; State = Valid }
                { model.CurrentForm with FirstName = firstNameField }
            | LastName ->
                let lastNameField = { model.CurrentForm.LastName with Content = value; State = Valid }
                { model.CurrentForm with LastName = lastNameField }
            | Email ->
                let emailField = { model.CurrentForm.Email with Content = value; State = Valid }
                { model.CurrentForm with Email = emailField }
            | Password ->
                let passwordField = { model.CurrentForm.Password with Content = value; State = Valid }
                { model.CurrentForm with Password = passwordField }
            | ConfirmPassword ->
                let confirmPasswordField = { model.CurrentForm.ConfirmPassword with Content = value; State = Valid }
                { model.CurrentForm with ConfirmPassword = confirmPasswordField }
            | ResetToken ->
                let resetTokenField = { model.CurrentForm.ResetToken with Content = value; State = Valid }
                { model.CurrentForm with ResetToken = resetTokenField }

        { model with CurrentForm = updatedForm }, Cmd.none, Intent.DoNothing

    | ResetForm ->
        { model with CurrentForm = defaultForm }, Cmd.none, Intent.DoNothing

    | ChangeMode formMode ->
        match model.CurrentMode, formMode with
        | PasswordReset PasswordResetMode.ForgotPassword, PasswordReset PasswordResetMode.ResetPassword ->
            let resetForm =
                {
                    model.CurrentForm with
                        ResetToken = { model.CurrentForm.ResetToken with Content = ""; State = Valid }
                        Password = { model.CurrentForm.Password with Content = ""; State = Valid }
                        ConfirmPassword = { model.CurrentForm.ConfirmPassword with Content = ""; State = Valid }
                }
            { model with CurrentMode = formMode; CurrentForm = resetForm }, Cmd.none, Intent.DoNothing
        | current, newMode when current = newMode ->
            model, Cmd.none, Intent.DoNothing
        | _ ->
            { model with CurrentMode = formMode }, ResetForm |> Cmd.ofMsg, Intent.DoNothing

    | UnknownError exn ->
        printfn $"❌An unknown error occurred: {exn.Message}"
        model, Cmd.none, Intent.UserAuthed

open Feliz

let overlay =
    Html.div [
        prop.className
            "fixed inset-0 z-40 bg-gradient-to-b from-dark-secondary/90 via-dark-secondary/40 to-dark-secondary/90 justify-center items-center flex"
        prop.children [
            Logo.cookieCreamLogo "register-page-logo"
        ]
    ]

let fieldInput dispatch (field: Field) =
    Typography.Input
        field.Content
        (fun newValue -> (field.FieldType, newValue) |> UpdateForm |> dispatch)
        (field.FieldType |> Field.toString)
        (field.ExpectedType |> FieldContentType.toString)
        Typography.MD Typography.Primary
        (field.State |> FieldState.toBool)
        (match field.State with Invalid err -> err | Valid -> "")
        ""

let registerHeader dispatch =
    Html.div [
        prop.className "flex flex-col items-center gap-4 mb-6"
        prop.children [
            Typography.Typography "Create your account" Typography.XXXXL Typography.Primary "text-center mb-2 drop-shadow-lg font-extrabold"
            Typography.Typography "Join CookieCream - the coziest corner of the internet ☕" Typography.SM Typography.Secondary "text-center"

            Html.p [
                prop.className "text-center text-sm text-text-accent mt-3"
                prop.children [
                    Html.text "Already have an account? "
                    Html.a [
                        prop.className "underline hover:text-accent transition cursor-pointer"
                        prop.onClick (fun _ -> Login |> ChangeMode |> dispatch)
                        prop.text "Log in"
                    ]
                ]
            ]
        ]
    ]

let loginHeader dispatch =
    Html.div [
        prop.className "flex flex-col items-center gap-4 mb-6"
        prop.children [
            Typography.Typography "Welcome back" Typography.XXXXL Typography.Primary "text-center mb-2 drop-shadow-lg font-extrabold"
            Typography.Typography "- Sign in to continue ☕ -" Typography.SM Typography.Secondary "text-center"

            Html.p [
                prop.className "text-center text-sm text-text-accent mt-3"
                prop.children [
                    Html.text "Not a member yet? "
                    Html.a [
                        prop.className "underline hover:text-accent transition cursor-pointer"
                        prop.onClick (fun _ -> Register |> ChangeMode |> dispatch)
                        prop.text "Register"
                    ]
                ]
            ]
        ]
    ]

let validationButton makeRequest currentMode currentForm =
    let buttonText =
        match currentMode with
        | Login -> "Log In"
        | Register -> "Sign Up"
        | PasswordReset PasswordResetMode.ForgotPassword -> "Send Token"
        | PasswordReset PasswordResetMode.ResetPassword -> "Reset Password"

    let buttonAction =
        match currentMode with
        | Login ->
            fun _ ->
                currentForm |> AuthForm.toLoginRequest |> makeRequest
        | Register ->
            fun _ ->
                currentForm |> AuthForm.toRegisterRequest |> makeRequest
        | PasswordReset mode ->
            match mode with
            | PasswordResetMode.ForgotPassword ->
                fun _ ->
                    currentForm |> AuthForm.toForgotPasswordRequest |> makeRequest
            | PasswordResetMode.ResetPassword ->
                fun _ ->
                    currentForm |> AuthForm.toResetPasswordRequest |> makeRequest

    Button ButtonColor.Normal buttonText buttonAction ""

let currentFormFields dispatch currentMode currentForm =
    let registerFormFields =
        [
            currentForm.Username
            currentForm.FirstName
            currentForm.LastName
            currentForm.Email
            currentForm.Password
            currentForm.ConfirmPassword
        ]

    let loginFormFields =
        [
            currentForm.Username
            currentForm.Password
        ]

    let forgotPasswordFormFields =
        [
            currentForm.Email
        ]

    let resetPasswordFormFields =
        [
            currentForm.Email
            currentForm.ResetToken
            currentForm.Password
            currentForm.ConfirmPassword
        ]

    let currentFields =
        match currentMode with
        | Login -> loginFormFields
        | Register -> registerFormFields
        | PasswordReset PasswordResetMode.ForgotPassword -> forgotPasswordFormFields
        | PasswordReset PasswordResetMode.ResetPassword -> resetPasswordFormFields

    Html.div [
        prop.className "flex flex-col gap-4"
        prop.children [
            yield! currentFields |> List.map (fieldInput dispatch)
        ]
    ]

let passwordResetField dispatch currentMode =
    match currentMode with
    | PasswordReset _ ->
        Html.p [
            prop.className "text-center text-sm text-text-accent mt-3"
            prop.children [
                Html.text "Remember? "
                Html.a [
                    prop.className "underline hover:text-accent transition cursor-pointer"
                    prop.onClick (fun _ -> Register |> ChangeMode |> dispatch)
                    prop.text "Back to authentication "
                ]
            ]
        ]
    | _ ->
        Html.p [
            prop.className "text-center text-sm text-text-accent mt-3"
            prop.children [
                Html.text "Forgot your password? "
                Html.a [
                    prop.className "underline hover:text-accent transition cursor-pointer"
                    prop.onClick (fun _ -> PasswordResetMode.ForgotPassword |> PasswordReset |> ChangeMode |> dispatch)
                    prop.text "Reset it here"
                ]
            ]
        ]

let view model dispatch =
    Html.div [
        prop.className "
            flex items-center justify-center
            pt-20
        "

        prop.children [
            Container ContainerSize.Medium "" [
                match model.CurrentMode with
                | Login -> loginHeader dispatch
                | Register -> registerHeader dispatch
                | PasswordReset PasswordResetMode.ForgotPassword ->
                    Html.div [
                        prop.className "flex flex-col items-center gap-4 mb-6"
                        prop.children [
                            Typography.Typography "Forgot your password?" Typography.XXXXL Typography.Primary "text-center mb-2 drop-shadow-lg font-extrabold"
                            Typography.Typography "Enter your email to receive a password reset token." Typography.SM Typography.Secondary "text-center"
                        ]
                    ]
                | PasswordReset PasswordResetMode.ResetPassword ->
                    Html.div [
                        prop.className "flex flex-col items-center gap-4 mb-6"
                        prop.children [
                            Typography.Typography "Reset your password" Typography.XXXXL Typography.Primary "text-center mb-2 drop-shadow-lg font-extrabold"
                            Typography.Typography "Enter the token sent to your email along with your new password." Typography.SM Typography.Secondary "text-center"
                        ]
                    ]

                currentFormFields dispatch model.CurrentMode model.CurrentForm

                passwordResetField dispatch model.CurrentMode

                validationButton (Start >> AuthUser >> dispatch) model.CurrentMode model.CurrentForm
            ]
        ]
    ]
