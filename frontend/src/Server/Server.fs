namespace Server

open SAFE
open Saturn
open Shared
open Giraffe

open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open MoviesApi
open AuthApi
open Microsoft.Extensions.Configuration
open DotNetEnv

module Server =

    // let moviesHandler =
    //     Remoting.createApi()
    //     |> Remoting.fromValue moviesApi
    //     |> Remoting.withRouteBuilder (fun typeName methodName -> $"/api/{typeName}/{methodName}")
    //     |> Remoting.buildHttpHandler
    let moviesHandler = Api.make moviesApi

    // let userHandler  =
    //     Remoting.createApi()
    //     |> Remoting.fromValue userApi
    //     |> Remoting.withRouteBuilder (fun typeName methodName -> $"/api/{typeName}/{methodName}")
    //     |> Remoting.buildHttpHandler

    let authHandler = Api.make authApi

    let webApp = choose [
        authHandler
        moviesHandler
        GET >=> routef "/%s" (fun _ -> htmlFile "public/index.html")
        GET >=> routef "/%s/%s" (fun _ -> htmlFile "public/index.html")
    ]

    let app = application {
        use_router webApp
        memory_cache
        use_static "public"
        use_gzip
        host_config (fun hostBuilder ->
            hostBuilder.ConfigureAppConfiguration(fun hostContext configBuilder ->
                let contentRoot = hostContext.HostingEnvironment.ContentRootPath
                printfn $"contentRoot: {contentRoot}"
                let envPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(contentRoot, "../../../.env"))
                printfn $"Loading env from: {envPath}"
                envPath |> Env.Load |> ignore

                configBuilder.AddEnvironmentVariables()
                |> ignore
            )
        )
    }

    [<EntryPoint>]
    let main _ =
        run app
        0
