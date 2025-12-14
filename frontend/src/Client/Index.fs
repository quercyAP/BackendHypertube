module Index

open Elmish
open SAFE
open Shared
open Feliz.Router

// open Browser.Dom

open Client.Components
open Client.Components.Common
open Client.Pages

let userApi = Api.makeProxy<IAuthApi> ()

type Model = {
    PageContext: PageContext
    HeaderModel: Header.Model
    FooterModel: Footer.Model
    LandingPageModel: LandingPage.Model
    AuthPageModel: AuthPage.Model
    MoviePageModel: MoviePage.Model
    CurrentPage: Page
}

type Msg =
    | HeaderMsg of Header.Msg
    | FooterMsg of Footer.Msg
    | LandingPageMsg of LandingPage.Msg
    | AuthPageMsg of AuthPage.Msg
    | MoviePageMsg of MoviePage.Msg

    | CheckAuth of ApiCall<unit, Result<AuthApiResult, AuthApiError>>
    | LogOutUser of ApiCall<unit, Result<AuthApiResult, AuthApiError>>
    | RefreshToken of ApiCall<unit, Result<AuthApiResult, AuthApiError>>

    | UrlChanged of string list
    | NavigateTo of Page
    | UnknownError of exn

let parseUrl url =
    printfn "current url segments: %A" url

    match url with
    | ["movies"] -> Page.Movies
    | ["movie"; id ] -> Page.MoviePage id
    | ["auth"] -> Page.AuthPage AuthMode.Login
    | ["auth"; "password-reset"; "forgot"] -> Page.AuthPage (AuthMode.PasswordReset PasswordResetMode.ForgotPassword)
    | ["auth"; "password-reset"; "reset"] -> Page.AuthPage (AuthMode.PasswordReset PasswordResetMode.ResetPassword)
    | _ -> Page.NotFound

let init () =
    let page = Router.currentUrl () |> parseUrl

    let headerModel, headerCmd = Header.init ()
    let footerModel, footerCmd = Footer.init ()
    let landingPageModel, landingPageCmd = LandingPage.init ()
    let authPageModel, authPageCmd = AuthPage.init AuthMode.Login
    let moviePageModel, moviePageCmd = MoviePage.init ()

    let initialContext = {
        IsAuthenticated = false
        CurrentUser = None
    }

    let initialModel = {
        PageContext = initialContext
        HeaderModel = headerModel
        FooterModel = footerModel
        LandingPageModel = landingPageModel
        AuthPageModel = authPageModel
        MoviePageModel = moviePageModel
        CurrentPage = page
    }

    let cmds = Cmd.batch [
        () |> Start |> CheckAuth |> Cmd.ofMsg
        Cmd.map HeaderMsg headerCmd
        Cmd.map FooterMsg footerCmd
        Cmd.map LandingPageMsg landingPageCmd
        Cmd.map AuthPageMsg authPageCmd
        Cmd.map MoviePageMsg moviePageCmd
    ]

    initialModel, cmds

let checkAuthCmd =
    () |> Start |> CheckAuth |> Cmd.ofMsg

let update msg model =

    match msg with
    | UrlChanged segments -> { model with CurrentPage = parseUrl segments }, Cmd.none

    | NavigateTo page ->
        let newUrl =
            match page with
            | Movies -> [| "movies" |]
            | AuthPage _ -> [| "auth" |]
            | MoviePage id -> [| "movie"; id |]
            | NotFound -> [| "movies" |]

        model, Cmd.ofEffect(fun _ -> Router.navigatePath(newUrl))

    | HeaderMsg msg ->
        let updatedHeaderModel, cmd, intent = Header.update msg model.HeaderModel

        match intent with
        | Header.Intent.DoNothing ->
            { model with HeaderModel = updatedHeaderModel }, Cmd.map HeaderMsg cmd
        | Header.Intent.NavigateTo page ->
            match page with
            | AuthPage mode ->
                // Reset AuthPageModel when navigating to AuthPage
                let newAuthPageModel, newAuthPageCmd = AuthPage.init mode
                let updatedModel = {
                    model with
                        HeaderModel = updatedHeaderModel
                        AuthPageModel = newAuthPageModel
                }
                let combinedCmds = Cmd.batch [
                    Cmd.map HeaderMsg cmd
                    Cmd.map AuthPageMsg newAuthPageCmd
                    checkAuthCmd
                    page |> NavigateTo |> Cmd.ofMsg
                ]
                updatedModel, combinedCmds
            | _ ->
                let cmds = Cmd.batch [
                    Cmd.map HeaderMsg cmd
                    checkAuthCmd
                    page |> NavigateTo |> Cmd.ofMsg
                ]
                { model with HeaderModel = updatedHeaderModel; }, cmds

        | Header.Intent.LogOutUser ->
            let cmds = Cmd.batch [
                Cmd.map HeaderMsg cmd
                () |> Start |> LogOutUser |> Cmd.ofMsg
            ]
            { model with HeaderModel = updatedHeaderModel }, cmds

    | FooterMsg msg ->
        let updatedFooterModel, cmd = Footer.update msg model.FooterModel
        { model with FooterModel = updatedFooterModel }, Cmd.map FooterMsg cmd

    | LandingPageMsg msg ->
        let updatedLandingPageModel, cmd, intent = LandingPage.update msg model.LandingPageModel
        let intentCmd =
            match intent with
            | LandingPage.Intent.DoNothing ->
                Cmd.none
            | LandingPage.Intent.NavigateTo page ->
                page |> NavigateTo |> Cmd.ofMsg
        { model with LandingPageModel = updatedLandingPageModel },
        Cmd.batch [
            intentCmd;
            Cmd.map LandingPageMsg cmd
        ]

    | AuthPageMsg msg ->
        let updatedAuthPageModel, authCmd, authIntent = AuthPage.update msg model.AuthPageModel

        let intentCmds =
            match authIntent with
            | AuthPage.DoNothing ->
                [ Cmd.none ]
            | AuthPage.UserAuthed ->
                [ Header.NavLink.Movies |> Header.Msg.SelectLink |> HeaderMsg |> Cmd.ofMsg
                  checkAuthCmd
                  ]

        { model with AuthPageModel = updatedAuthPageModel }, Cmd.batch ([ Cmd.map AuthPageMsg authCmd ] @ intentCmds)

    | MoviePageMsg msg ->
        let updatedMoviePageModel, cmd, intent = MoviePage.update msg model.MoviePageModel
        let intentCmd =
            match intent with
            | MoviePage.Intent.DoNothing ->
                Cmd.none
            | MoviePage.Intent.NavigateTo page ->
                page |> NavigateTo |> Cmd.ofMsg
        { model with MoviePageModel = updatedMoviePageModel },
        Cmd.batch [
            intentCmd;
            Cmd.map MoviePageMsg cmd
        ]

    | LogOutUser apiCall ->
        match apiCall with
        | Start () ->
            let logOutCmd =
                Cmd.OfAsync.either
                    userApi.revoke
                    ()
                    (Finished >> LogOutUser)
                    UnknownError
            model, logOutCmd
        | Finished result ->
            match result with
            | Ok _ ->
                printfn "User logged out successfully."
                let cmd = Header.NavLink.Movies |> Header.Msg.SelectLink |> HeaderMsg |> Cmd.ofMsg
                model, Cmd.batch [ cmd; checkAuthCmd ]

            | Error err ->
                printfn $"Error logging out: {err}"
                model, checkAuthCmd

    | CheckAuth apiCall ->
        match apiCall with
        | Start () ->
            let checkAuthCmd = Cmd.OfAsync.perform userApi.profile () (Finished >> CheckAuth)
            model, checkAuthCmd
        | Finished result ->
            let isAuthenticated, currentUser =
                match result with
                | Ok (AuthApiResult.UserDto user) ->
                    printfn $"Is authenticated: {user}"
                    true, Some user
                | Error err ->
                    printfn $"Auth check error: {err}"
                    match err with
                    | AuthApiError.JsonDecodingError e ->
                        printfn $"JSON Decoding Error: {e}"
                    | AuthApiError.TokenExpiredError ->
                        printfn "Token Expired"
                    | _ ->
                        printfn $"Other Auth API Error: {err}"
                    false, None
                | _ ->
                    printfn "Unexpected result from auth check."
                    false, None

            let updatedPageContext = { model.PageContext with IsAuthenticated = isAuthenticated; CurrentUser = currentUser }
            { model with PageContext = updatedPageContext }, Cmd.none

    | RefreshToken apiCall ->
        match apiCall with
        | Start refreshToken ->
            let refreshCmd =
                Cmd.OfAsync.either
                    userApi.refresh
                    refreshToken
                    (Finished >> RefreshToken)
                    UnknownError
            model, refreshCmd
        | Finished result ->
            match result with
            | Ok authResult ->
                printfn $"Token refreshed successfully: {authResult}"
                model, Cmd.none
            | Error err ->
                printfn $"Error refreshing token: {err}"
                model, Cmd.none

    | UnknownError exn ->
        printfn $"An unknown error occurred: {exn.Message}"
        model, Cmd.none

open Feliz

let view model dispatch =
    Html.section [
        prop.className "flex flex-col overflow-x-hidden bg-secondary overflow-y-auto min-h-screen min-w-screen"

        prop.children [
            // Meta viewport tag for proper mobile scaling
            Html.meta [
                prop.name "viewport"
                prop.content "width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"
            ]

            Header.view model.PageContext model.HeaderModel (HeaderMsg >> dispatch)

            Html.button [
                prop.className "btn btn-primary fixed bottom-4 right-4 z-50"
                prop.text "Check Auth"
                prop.onClick (fun _ -> () |> Start |> CheckAuth |> dispatch)
            ]

            Html.button [
                prop.className "btn btn-primary fixed bottom-8 right-4 z-50"
                prop.text "Refresh Token"
                prop.onClick (fun _ -> () |> Start |> RefreshToken |> dispatch)
            ]

            let text =
                match model.PageContext.CurrentUser with
                | None -> "No user"
                | Some user ->
                    $"User: {user.username}, Email: {user.email}"

            Html.text $"{text}"

            Html.main [
                prop.className "flex-grow"
                prop.children [
                    React.router [
                        router.pathMode
                        router.onUrlChanged (UrlChanged >> dispatch)
                        router.children [
                            match model.CurrentPage with
                            | Movies ->
                                LandingPage.view model.PageContext model.LandingPageModel (LandingPageMsg >> dispatch)
                            | AuthPage mode ->
                                match mode with
                                | Login
                                | Register
                                | PasswordReset _ ->
                                    AuthPage.view model.AuthPageModel (AuthPageMsg >> dispatch)
                            | MoviePage id ->
                                MoviePage.view (Some id) model.PageContext model.MoviePageModel (MoviePageMsg >> dispatch)
                            | NotFound ->
                                Html.div [
                                    prop.className "flex flex-col items-center justify-center h-full w-full"
                                    prop.children [
                                        Container ContainerSize.Small "" [
                                            Typography.Typography "404 - Page Not Found" Typography.XXXL Typography.Primary "text-center"
                                            Html.div [
                                                prop.className "mt-6 flex gap-4 justify-center"
                                                prop.children [
                                                    Button ButtonColor.Normal "Go to Home" (fun () -> Page.Movies |> NavigateTo |> dispatch) "animate-pulse"
                                                ]
                                            ]
                                        ]
                                    ]
                                ]
                        ]
                    ]
                ]
            ]

            Footer.view model.FooterModel (FooterMsg >> dispatch)
        ]
    ]
