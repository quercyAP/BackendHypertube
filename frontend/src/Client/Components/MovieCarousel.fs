module Client.Components.MovieCarousel

open Feliz
open Feliz.Swiper
open Browser
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop

let gsap = importDefault<obj> "gsap"

type ScrollDirection =
    | Left
    | Right


let SwiperMovieCarousel (movies: ReactElement list) (reverse: bool) =
    swiper [
        prop.custom("modules", [| autoplayModule |])

        Swiper.spaceBetween 1
        Swiper.slidesPerView 7

        // // breakpoints Swiper (px)
        // Swiper.breakpoints (
        //     createObj [
        //         "640"  ==> createObj [ "slidesPerView" ==> 1 ]   // sm
        //         "768"  ==> createObj [ "slidesPerView" ==> 2 ]   // md
        //         "1024" ==> createObj [ "slidesPerView" ==> 4 ]   // lg
        //     ]
        // )

        prop.custom("loop", true)
        prop.custom("allowTouchMove", false)

        Swiper.children [
            yield! movies
            |> List.map (fun movie ->
                swiperSlide [
                    Swiper.children [
                        movie
                    ]
                ]
            )
        ]
    ]

[<ReactComponent>]
let MovieCarousel (movies: ReactElement list) (scrollDirection: ScrollDirection) =
    let containerRef = React.useRef<HTMLDivElement option>(None)
    let tweenRef = React.useRef<obj option>(None)

    // Effet réactif → se relance si le nombre d'éléments change
    React.useEffect(
        (fun () ->
            match containerRef.current with
            | Some el ->
                // cleanup d'un éventuel tween précédent
                match tweenRef.current with
                | Some t -> t?kill() |> ignore
                | None -> ()

                let scrollWidth = el.scrollWidth - el.clientWidth
                if scrollWidth > 0 then
                    match scrollDirection with
                    | Left ->
                        el.scrollLeft <- 0.0
                    | Right ->
                        el.scrollLeft <- scrollWidth

                    let target =
                        match scrollDirection with
                        | Left -> scrollWidth
                        | Right -> 0.0

                    let tween =
                        gsap?``to``(
                            el,
                            createObj [
                                "scrollLeft" ==> target
                                "duration" ==> el.children.length * 3
                                "ease" ==> "none"
                                "repeat" ==> -1
                                "yoyo" ==> true
                            ]
                        )
                    tweenRef.current <- Some tween
                    printfn $"[GSAP] Animation (scrollWidth={scrollWidth})"
                else
                    printfn "[GSAP] Rien à scroller"

            | None -> ()

            // cleanup au démontage
            React.createDisposable(fun () ->
                match tweenRef.current with
                | Some t -> t?kill() |> ignore
                | None -> ()
            )
        ),
        [| box movies.Length |]
    )

    // pause/reprise au survol
    let onMouseEnter _ =
        match tweenRef.current with
        | Some t -> t?pause() |> ignore
        | None -> ()

    let onMouseLeave _ =
        match tweenRef.current with
        | Some t -> t?resume() |> ignore
        | None -> ()

    Html.div [
        prop.ref containerRef
        // prop.className "flex flex-row space-x-4 overflow-x-scroll p-4 w-full cursor-grab scrollbar-hidden"
        prop.className "flex flex-row space-x-6 w-screen overflow-hidden p-4 select-none"

        // prop.onMouseEnter onMouseEnter
        // prop.onMouseLeave onMouseLeave
        prop.children movies // duplication → loop fluide
    ]
