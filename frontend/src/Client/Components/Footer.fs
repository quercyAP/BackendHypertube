module Client.Components.Footer

open Client.Components
open Client.Components.Typography

open Shared
open Elmish

type Model = {
    PlaceHolder: string
}

type Msg =
    | NoOp

let init () = { PlaceHolder = "" }, Cmd.none

let update msg model =
    match msg with
    | NoOp -> model, Cmd.none

open Feliz

let view model dispatch =
    Html.footer [
        Html.div [
            prop.classes [
                "w-full z-50 transition-colors duration-300 "
                "bg-gradient-to-t from-dark-secondary/90 to-transparent"
            ]

            prop.children [
                Html.div [
                    prop.className "px-4 pt-20 pb-5 flex flex-row justify-center items-center mx-auto"
                    prop.children [
                        Html.div [
                            prop.className "hidden sm:block"
                            prop.children [
                                Logo.cookieCreamCup "footer"
                            ]
                        ]
                        Typography "© 2025 CookieCream. All rights reserved." SM Primary ""
                    ]
                ]
            ]
        ]
    ]
