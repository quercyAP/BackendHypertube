module Feliz.ReactPlayer

open Fable.Core
open Fable.Core.JsInterop
open Feliz

[<ImportDefault("react-player")>]
let private reactPlayerComp: obj = jsNative

let reactPlayer (props: IReactProperty list) : ReactElement =
    Interop.reactApi.createElement(reactPlayerComp, createObj !!props)

[<RequireQualifiedAccess>]
module ReactPlayer =
    let src (u: string) = prop.custom("src", u)
    let playing (v: bool) = prop.custom("playing", v)
    let controls (v: bool) = prop.custom("controls", v)
    let loop (v: bool) = prop.custom("loop", v)
    let muted (v: bool) = prop.custom("muted", v)
    let width (v: string) = prop.custom("width", v)
    let height (v: string) = prop.custom("height", v)
    let aspectRatio (v: string) = prop.custom("aspectRatio", v)
    let style (o: obj) = prop.custom("style", o)

    // Callbacks
    let onReady (f: unit -> unit) = prop.custom("onReady", f)
    let onPlay (f: unit -> unit) = prop.custom("onPlay", f)
    let onPause (f: unit -> unit) = prop.custom("onPause", f)
    let onEnded (f: unit -> unit) = prop.custom("onEnded", f)
    let onError (f: obj -> unit) = prop.custom("onError", f)

    // Config avancée (HLS, etc.)
    let config (o: obj) = prop.custom("config", o)

    let reactPlayerResponsiveStyle =
    // { width: '100%', height: 'auto', aspectRatio: '16/9' }
        createObj [
            "width" ==> "100%"
            "height" ==> "auto"
            "aspectRatio" ==> "16/9"
        ]
