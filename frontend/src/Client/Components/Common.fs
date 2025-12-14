module Client.Components.Common

open Feliz

type ContainerSize =
    | Small
    | Medium
    | Large

module ContainerSize =
    let toClasses size =
        match size with
        | Small -> "min-w-[300px] max-w-[500px] rounded-3xl"
        | Medium -> "md:min-w-[400px] md:max-w-[700px] md:w-full w-screen md:rounded-3xl"
        | Large -> "md:min-w-[600px] md:max-w-[900px] md:w-full w-screen md:rounded-3xl"

[<ReactComponent>]
let Container (containerSize: ContainerSize) (additionalClasses: string) (children: ReactElement list) =
    Html.div [
        prop.className $"
            flex flex-col gap-6
            {containerSize |> ContainerSize.toClasses}
            p-10
            backdrop-blur-lg
            bg-dark-secondary/70
            shadow-2xl shadow-dark-primary/40
            border border-accent/20
            animate-fade-in
            {additionalClasses}
        "

        prop.children [
            yield! children
        ]
    ]

[<ReactComponent>]
let Overlay (additionalClasses: string) (children: ReactElement list) =
    Html.div [
        prop.className $"
            fixed inset-0 z-40
            bg-gradient-to-b from-dark-secondary/90 via-dark-secondary/40 to-dark-secondary/90
            justify-center items-center flex
            {additionalClasses}
        "
        prop.children [
            yield! children
        ]
    ]

type ButtonColor =
    | Normal
    | Danger

[<RequireQualifiedAccess>]
module ButtonColor =
    let toClass color =
        match color with
        | Normal -> "from-secondary via-accent to-secondary"
        | Danger -> "from-error via-error-accent to-error"

[<ReactComponent>]
let Button (color: ButtonColor) (text: string) (onClick: unit -> unit) (additionalClasses: string) =
    Html.button [
        prop.className $"
            py-3 px-6 rounded-xl
            bg-gradient-to-br {ButtonColor.toClass color}
            text-dark font-semibold
            hover:brightness-110
            transition-all duration-300
            shadow-cookie
            {additionalClasses}
        "
        prop.text text
        prop.onClick (fun _ -> onClick())
    ]
