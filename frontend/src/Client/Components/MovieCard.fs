module Client.Components.MovieCard

open Feliz
open Shared

[<ReactComponent>]
let MovieCard (movie: MovieDto) (onClick: unit -> unit)=

    let coverImageUrl =
        movie.coverImageUrl
        |> function
            | Some url -> url
            | None -> ""

    let yearCaption =
        movie.year
        |> function
            | Some year ->
                Typography.Typography (year.ToString()) Typography.XS Typography.Primary "opacity-80"
            | None -> Html.none

    let ratingCaption =
        movie.rating
        |> function
            | Some rating ->
                Typography.Typography ($"⭐ {rating.ToString()}") Typography.XS Typography.Primary "opacity-80"
            | None -> Html.none

    Html.div [
        prop.className
            "relative w-48 h-72 rounded-xl overflow-hidden shadow-lg flex-shrink-0 cursor-pointer swiper-lazy-preloaded"
        prop.custom("loading", "lazy")
        prop.style [
            style.backgroundImage $"url(https://image.tmdb.org/t/p/w500{coverImageUrl})"
            style.backgroundSize.cover
        ]
        prop.children [
            Html.div [
                prop.className
                    "absolute inset-0 flex flex-col justify-end p-3 text-white
                     bg-gradient-to-t from-black via-black/30 to-transparent"
                prop.children [
                    Typography.Typography movie.title Typography.SM Typography.Primary "font-semibold text-sm truncate"
                    yearCaption
                    ratingCaption
                ]
            ]
        ]
        prop.onClick (fun _ -> onClick())
    ]
