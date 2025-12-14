module Configuration

open Microsoft.Extensions.Configuration

type AppConfiguration = {
    BackendMoviesUrl: string
    BackendAuthUrl: string
}

let getConfiguration (config: IConfiguration) : AppConfiguration =
    {
        BackendMoviesUrl = config["HR_BACKEND_MOVIES_URL"]
        BackendAuthUrl  = config["HR_BACKEND_AUTH_URL"]
    }
