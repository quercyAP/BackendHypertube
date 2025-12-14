module Client.Components.Header

open Client.Components
open Client.Components.Common
open Client.Components.Typography

open Shared
open Elmish

type NavLink =
    | Movies
    | Register
    | Login

module NavLink =
    let toPage link =
        match link with
        | NavLink.Movies -> Page.Movies
        | NavLink.Register -> AuthMode.Register |> AuthPage
        | NavLink.Login -> AuthMode.Login |> AuthPage

type Model = {
    IsTransparent: bool
    IsMenuOpen: bool
    IsLogOutOverlayVisible: bool
    SelectedLink: NavLink
}

type LogOutMsg =
    | ShowLogOutOverlay
    | HideLogOutOverlay
    | ConfirmLogOut

type Msg =
    | ToggleTransparency of bool
    | ToggleMenu of bool
    | SelectLink of NavLink
    | LogOutMsg of LogOutMsg

type Intent =
    | DoNothing
    | NavigateTo of Page
    | LogOutUser

let init () =
    {
        IsTransparent = true
        SelectedLink = NavLink.Movies
        IsMenuOpen = false
        IsLogOutOverlayVisible = false
    }, Cmd.none

let update msg model =
    match msg with
    | ToggleTransparency newVal ->
        { model with IsTransparent = newVal }, Cmd.none, Intent.DoNothing
    | SelectLink link ->
        { model with SelectedLink = link },
        Cmd.none,
        link |> NavLink.toPage |> Intent.NavigateTo
    | ToggleMenu newVal ->
        { model with IsMenuOpen = newVal }, Cmd.none, Intent.DoNothing
    | LogOutMsg logOutMsg ->
        match logOutMsg with
        | ShowLogOutOverlay ->
            { model with IsLogOutOverlayVisible = true }, Cmd.none, Intent.DoNothing
        | HideLogOutOverlay ->
            { model with IsLogOutOverlayVisible = false }, Cmd.none, Intent.DoNothing
        | ConfirmLogOut ->
            { model with IsLogOutOverlayVisible = false }, Cmd.none, Intent.LogOutUser

open Feliz

let navLink text linkType selectedType dispatch =
    Html.li [
        Html.a [
            prop.href "#"
            prop.children [
                Typography text LG Primary (if linkType = selectedType then "underline" else "")
            ]

            prop.onClick (fun e ->
                e.preventDefault()
                SelectLink linkType |> dispatch
            )
        ]
    ]

// let navMenu model dispatch =
//     Html.nav [
//         Html.ul [
//             prop.className "hidden md:flex flex-row gap-4"
//             prop.children [
//                 navLink "Home" Home model.SelectedLink dispatch
//                 navLink "Library" Library model.SelectedLink dispatch
//             ]
//         ]
//
//         Html.button [
//             prop.className "md:hidden"
//             prop.onClick (fun _ -> model.IsMenuOpen |> not |> ToggleMenu |> dispatch)
//             prop.children [
//                 Typography "☰" LG Primary ""
//             ]
//         ]
//     ]

let logOutButton dispatch =
    Html.button [
        prop.className "w-6 h-6 ml-4 flex items-center justify-center hover:brightness-125 transition-all duration-200"
        prop.onClick (fun _ -> ShowLogOutOverlay |> LogOutMsg |> dispatch)
        prop.children [
            Logo.logoutLogo
        ]
    ]

let logInButton dispatch =
    Html.button [
        prop.className "w-6 h-6 ml-4 flex items-center justify-center hover:brightness-125 transition-all duration-200"
        prop.onClick (fun _ ->  NavLink.Login |> SelectLink |> dispatch)
        prop.children [
            Logo.loginLogo
        ]
    ]

let logOutOverlay dispatch =
    Overlay "" [
        Html.div [
            prop.className "flex flex-col items-center justify-center h-full w-full"
            prop.onClick (fun e ->
                e.stopPropagation()
                HideLogOutOverlay |> LogOutMsg |> dispatch
            )

            prop.children [
                Container ContainerSize.Small "" [
                    Html.img [
                        prop.classes [
                            "hidden sm:block"
                            "w-full max-w-[800px]"
                            "px-4 px-12"
                            "absolute inset-0 -top-1/3"
                            "transition-all duration-500 ease-in-out"
                        ]
                        prop.src "/logo.svg"
                    ]
                    Typography "Are you sure you want to leave ?" XXXL Primary "text-center"
                    Html.div [
                        prop.className "mt-6 flex gap-4 justify-center"
                        prop.children [
                            Button ButtonColor.Normal "Stay" (fun () -> HideLogOutOverlay |> LogOutMsg |> dispatch) "animate-pulse"
                            Button ButtonColor.Danger "Leave" (fun () -> ConfirmLogOut |> LogOutMsg |> dispatch ) ""
                        ]
                    ]
                ]
            ]
        ]
    ]

let logo dispatch =
    Html.a [
        prop.href "#"
        prop.children [
            Html.div [
                prop.className "block sm:hidden cursor-pointer"
                prop.children [
                    Logo.cookieCreamCup "header-cup-logo"
                ]
            ]
            Html.div [
                prop.className "hidden sm:block lg:hidden cursor-pointer"
                prop.children [
                    Logo.cookieCreamText "header-text-logo"
                ]
            ]
            Html.div [
                prop.className "hidden lg:block cursor-pointer"
                prop.children [
                    Logo.cookieCreamLogo "header-logo"
                ]
            ]
        ]

        prop.onClick (fun e ->
            e.preventDefault()
            SelectLink NavLink.Movies |> dispatch
        )
    ]

let view ctx model dispatch =

    let background =
        if model.IsTransparent then
            "bg-gradient-to-b from-dark-secondary/90 via-dark-secondary/60 to-transparent"
        else
            "bg-dark-primary/90 shadow-md"

    Html.header [
        Html.div [
            prop.classes [
                "w-full z-50 transition-colors duration-300"
                background
            ]
            prop.children [
                Html.div [
                    prop.className "px-4 py-3 flex items-center justify-between"
                    prop.children [
                        logo dispatch
                        Html.div [
                            prop.className "flex flex-row items-center"
                            prop.children [
                                // navMenu model dispatch
                                if ctx.IsAuthenticated then
                                    logOutButton dispatch
                                else
                                    logInButton dispatch
                            ]
                        ]
                    ]
                ]

                // if model.IsMenuOpen then
                //     Html.div [
                //         prop.className "md:hidden index-z-999 w-full py-4"
                //         prop.children [
                //             Html.ul [
                //                 prop.className "flex flex-col items-center gap-8"
                //                 prop.children [
                //                     navLink "Home" Home model.SelectedLink dispatch
                //                     navLink "Library" Library model.SelectedLink dispatch
                //                 ]
                //             ]
                //         ]
                //     ]
            ]
        ]
        if model.IsLogOutOverlayVisible then
            logOutOverlay dispatch
    ]
