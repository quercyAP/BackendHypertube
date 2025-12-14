module Client.Pages.MoviePage

open Client.Components
open Client.Components.Common
open Elmish
open SAFE
open Shared

open Feliz.ReactPlayer

type Model = {
    MovieId: string option
    MovieDetails: MovieDetailsDto option
    IsLoading: bool
    ErrorMessage: string option
}

type Msg =
    | LoadMovieDetails of ApiCall<string, Result<MovieDetailsDto, MoviesApiError>>
    | ChangedMovieId of string option
    | NavigateTo of Page

type Intent =
    | NavigateTo of Page
    | DoNothing

let moviesApi = Api.makeProxy<IMoviesApi> ()

let init () =
    let initialModel = {
        MovieId = None
        MovieDetails = None
        IsLoading = false
        ErrorMessage = None
    }
    initialModel, Cmd.none

let update msg model =
    match msg with
    | LoadMovieDetails apiCall ->
        match apiCall with
        | Start movieId ->
            let cmd = Cmd.OfAsync.perform moviesApi.getMovieById movieId (Finished >> LoadMovieDetails)
            { model with IsLoading = true; ErrorMessage = None }, cmd, Intent.DoNothing
        | Finished result ->
            match result with
            | Ok movieDetails ->
                { model with MovieDetails = Some movieDetails; IsLoading = false }, Cmd.none, Intent.DoNothing
            | Error err ->
                let errorMsg =
                    match err with
                    | MoviesApiError.HttpError msg -> $"HTTP Error: {msg}"
                    | MoviesApiError.JsonDecodingError msg -> $"Decoding Error: {msg}"
                    | MoviesApiError.TokenExpiredError -> "Session expired. Please log in again."
                { model with IsLoading = false; ErrorMessage = Some errorMsg }, Cmd.none, Intent.DoNothing
    | ChangedMovieId id ->
        let cmd =
            match id with
            | Some movieId -> LoadMovieDetails (Start movieId) |> Cmd.ofMsg
            | None -> Cmd.none
        { model with MovieId = id; MovieDetails = None; ErrorMessage = None }, cmd, Intent.DoNothing

    | Msg.NavigateTo page ->
        model, Cmd.none, Intent.NavigateTo page

open Feliz

let renderMovieDetails (movieDetails: MovieDetailsDto) =
    Html.div [
        prop.classes [
            "flex flex-col md:flex-row"
            "p-4"
            "items-center"
        ]
        prop.children [
            Html.div [
                prop.className "p-4 w-64 flex-shrink-0"
                prop.children [
                    Html.img [
                        prop.className "rounded-lg shadow-md"
                        prop.src $"https://image.tmdb.org/t/p/w500{movieDetails.coverImageUrl}"
                        prop.alt movieDetails.title
                    ]
                ]
            ]

            Html.div [
                prop.className "flex flex-col lg:flex-row"
                prop.children [
                    Html.div [
                        prop.className "flex-1 flex flex-col justify-center my-5 pl-3 border-l border-accent/20"
                        prop.children [
                            Typography.Typography (movieDetails.title.ToString()) Typography.XXXL Typography.Primary "font-bold mb-3"
                            Typography.Typography ($"⭐ {movieDetails.rating.ToString()}") Typography.XL Typography.Primary "opacity-80 mb-2"
                            Typography.Typography $"Directed by {movieDetails.director.ToString()}" Typography.MD Typography.Primary "mb-1"
                            Typography.Typography $"Duration {movieDetails.duration.ToString()} minutes" Typography.MD Typography.Primary "mb-1"
                            Typography.Typography $"{movieDetails.year.ToString()}, {movieDetails.genre.ToString()}" Typography.MD Typography.Primary "mb-2"
                            Typography.Typography $"Starring {movieDetails.cast.ToString()}" Typography.MD Typography.Primary "mb-1"
                        ]
                    ]
                    Html.div [
                        prop.className "flex-1 flex flex-col justify-center my-5 pl-3 border-l border-accent/20"
                        prop.children [
                            Typography.Typography (movieDetails.summary.ToString()) Typography.MD Typography.Accent "mb-2"
                        ]
                    ]
                ]
            ]
        ]
    ]

let authenticationOverlay dispatch =
    Overlay "" [
        Html.div [
            prop.className "flex flex-col items-center justify-center h-full w-full"

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
                    Typography.Typography "You need to be authenticated to access this page" Typography.XXXL Typography.Primary "text-center"
                    Html.div [
                        prop.className "mt-6 flex gap-4 justify-center"
                        prop.children [
                            Button ButtonColor.Normal "Go to authentication" (fun () -> (AuthPage Login) |> Msg.NavigateTo |> dispatch) "animate-pulse"
                            Button ButtonColor.Normal "Go to home" (fun () -> Movies |> Msg.NavigateTo |> dispatch) ""
                        ]
                    ]
                ]
            ]
        ]
    ]

[<ReactComponent>]
let movieViewer url =
    Html.div [
        prop.className "w-full h-full"
        prop.children [
            reactPlayer [
                ReactPlayer.src url
                ReactPlayer.controls true
                ReactPlayer.playing false
                ReactPlayer.width "100%"
                ReactPlayer.height "100%"
                ReactPlayer.aspectRatio "16/9"
            ]
        ]
    ]


let view movieId pageContext model dispatch =

    if not pageContext.IsAuthenticated then
        authenticationOverlay dispatch
    else

        if movieId <> model.MovieId then
            dispatch (ChangedMovieId movieId)

        Html.section [
            prop.className "flex flex-col overflow-x-hidden bg-secondary overflow-y-auto min-h-screen min-w-screen"
            prop.children [
                match model.IsLoading with
                | true ->
                    Html.div [
                        prop.className "flex justify-center items-center h-64"
                        prop.children [
                            Html.span [
                                prop.className "loader"
                            ]
                        ]
                    ]
                | false ->
                    match model.MovieDetails with
                    | Some details ->
                        Html.div [
                            prop.classes [
                                  "flex flex-col"
                                  "mx-4"
                                  "bg-gradient-to-b from-dark-primary via-dark-secondary to-secondary"
                                  "shadow-xl shadow-dark-primary/70"
                                  ]
                            prop.children [
                                Html.div [
                                    prop.className "flex-1"
                                    prop.children [
                                        Html.div [
                                            prop.className "w-full h-96"
                                            prop.children [
                                                movieViewer "https://www.youtube.com/watch?v=gy5UUvbhkdw"
                                            ]
                                        ]
                                    ]
                                ]
                                renderMovieDetails details
                            ]
                        ]
                    | None ->
                        Html.div [
                            prop.className "flex justify-center items-center text-accent"
                            prop.text "No movie details available."
                        ]
            ]
        ]
