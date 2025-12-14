module Client.Components.Typography

open Client.Components.Logo
open Feliz

type TextSize =
    | XS
    | SM
    | MD
    | LG
    | XL
    | XXL
    | XXXL
    | XXXXL

type TextColor =
    | Primary
    | Secondary
    | Accent

let Typography (text: string) (size: TextSize) (color: TextColor) (additionalClasses: string)=
    let sizeClass =
        match size with
        | XS -> "text-xs"
        | SM -> "text-sm"
        | MD -> "text-base"
        | LG -> "text-lg"
        | XL -> "text-xl"
        | XXL -> "text-2xl"
        | XXXL -> "text-3xl"
        | XXXXL -> "text-4xl"

    let colorClass =
        match color with
        | Primary -> "text-primary"
        | Secondary -> "text-secondary"
        | Accent -> "text-accent"

    Html.span [
        prop.className $"font-pacifico {sizeClass} {colorClass} text-shadow-lg {additionalClasses}"
        prop.text text
    ]

let Input
    (value:string)
    (onChange: string -> unit)
    (placeholder: string)
    (type': string)
    (size: TextSize)
    (color: TextColor)
    (isValid: bool)
    (error: string)
    (additionalClasses: string) =

    let sizeClass =
        match size with
        | XS -> "text-xs"
        | SM -> "text-sm"
        | MD -> "text-base"
        | LG -> "text-lg"
        | XL -> "text-xl"
        | XXL -> "text-2xl"
        | XXXL -> "text-3xl"
        | XXXXL -> "text-4xl"

    let colorClass =
        match color with
        | Primary -> "text-primary"
        | Secondary -> "text-secondary"
        | Accent -> "text-accent"

    Html.div [
        prop.classes [
            "flex flex-row items-center"
            "rounded-xl bg-dark-secondary/60 text-text-primary"
            if isValid |> not then
                "ring-1 ring-error"
            else
                "ring ring-transparent"
            "focus:outline-none focus:ring-2 focus:ring-accent focus:bg-dark-secondary/80 transition"
            "w-full px-4 py-3"
        ]
        prop.children [
            Html.input [
                prop.classes [
                    $"font-pacifico {sizeClass} {colorClass} text-shadow-lg {additionalClasses}"
                    "bg-transparent w-full outline-none"
                    "placeholder-text-accent placeholder-font-pacifico"
                ]
                prop.placeholder placeholder
                prop.type' type'
                prop.value value
                prop.onChange onChange
            ]

            if (isValid |> not) then
                Html.div [
                    prop.className "relative h-full w-6 group"
                    prop.children [
                        infoLogo (isValid |> not)
                        Html.div [
                            prop.classes [
                                "absolute bottom-full right-1/2 translate-x-1/4 mb-2"
                                "invisible group-hover:visible"
                                "opacity-0 group-hover:opacity-75 -transition-opacity duration-150"
                                "bg-error text-white text-sm px-3 py-2 rounded-lg whitespace-nowrap"
                                "z-10"
                            ]
                            prop.text error
                        ]
                    ]
                ]
            else
                Html.none

        ]
    ]
