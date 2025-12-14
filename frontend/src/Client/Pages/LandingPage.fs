module Client.Pages.LandingPage

open System
open Elmish
open SAFE
open Shared

open Client.Components
open Client.Components.Common
open Client.Components.Typography
open Client.Components.MovieCard
open Client.Components.MovieCarousel

type Model = {
    MoviesList: RemoteData<MovieDto list>
}

type Msg =
    | NavigateTo of Page
    | LoadMovies of ApiCall<unit, Result<MovieListResponse, MoviesApiError>>

type Intent =
    | NavigateTo of Page
    | DoNothing

let moviesApi = Api.makeProxy<IMoviesApi> ()

let init () =
    let initialModel = {  MoviesList = NotStarted }
    let initialCmd = Cmd.batch [
        LoadMovies(Start () ) |> Cmd.ofMsg
    ]

    initialModel, initialCmd

let update msg model =
    match msg with
    | LoadMovies msg ->
        match msg with
        | Start() ->
            let loadMoviesCmd = Cmd.OfAsync.perform moviesApi.listMovies () (Finished >> LoadMovies)
            { model with MoviesList = Loading None }, loadMoviesCmd, Intent.DoNothing

        | Finished result ->
            match result with
            | Ok movies ->
                { model with MoviesList = Loaded movies.movies }, Cmd.none, Intent.DoNothing
            | Error err ->
                printfn $"Movies loading error: {err}"
                model, Cmd.none, Intent.DoNothing

    | Msg.NavigateTo page ->
        model, Cmd.none, Intent.NavigateTo page

open Feliz

let overlay =
    Html.div [
        prop.className
            "fixed inset-0 z-40 bg-gradient-to-b from-dark-secondary/90 via-dark-secondary/40 to-dark-secondary/90 justify-center items-center flex"
        prop.children [
            Html.img [
                prop.className "w-full max-w-[1000px] xl:max-w-[1400px] px-4 sm:px-8 md:px-12 lg:px-12 transition-all duration-500 ease-in-out"
                prop.src "/logo.svg"
            ]
        ]
    ]

let renderSlider (movies: MovieDto list) direction dispatch =
    Html.div [
        prop.className "w-screen"
        prop.children [
            let cards =
                movies
                |> List.map (fun movie ->
                    (movie, (fun _ -> movie.id.ToString() |> MoviePage |> Msg.NavigateTo |> dispatch))
                    ||> MovieCard
                    )
            SwiperMovieCarousel cards direction
        ]
    ]

let renderSliders (moviesList: RemoteData<MovieDto list>) dispatch =
    let movies =
        (match moviesList with
        | NotStarted | Loading(None) ->
            List.init 30 (fun _ ->
                {
                    id = Guid.Empty
                    imdbId = None
                    genre = None
                    title = "Loading..."
                    coverImageUrl = None
                    year = None
                    rating = None
                    isWatched = false
                }
            )
        | Loading (Some movies) | Loaded movies -> movies)
        |> List.splitInto 3

    Html.div [
        prop.className "flex flex-col gap-8"
        prop.children [
            yield! movies
            |> List.mapi (fun i elt ->
                renderSlider elt (i % 2 = 0) dispatch
            )
        ]
    ]

let view pageContext model dispatch =
    Html.div [
        Html.div [
            prop.className "h-full w-full"
            prop.children [
                Html.div [
                    prop.className "flex flex-col items-center justify-center h-full"
                    prop.children [
                        Html.div [
                            prop.className "w-full px-4 overflow-x-hidden"
                            prop.children [
                                renderSliders model.MoviesList dispatch
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]
