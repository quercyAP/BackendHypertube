module Feliz.Swiper

open Feliz
open Fable.Core
open Fable.Core.JsInterop

// On importe les composants JS Swiper / SwiperSlide
[<Import("Swiper", "swiper/react")>]
let private swiperComp: obj = jsNative

[<Import("SwiperSlide", "swiper/react")>]
let private swiperSlideComp: obj = jsNative

[<Import("Autoplay", "swiper/modules")>]
let autoplayModule : obj = jsNative

// Composants Feliz
let swiper (props: IReactProperty list) : ReactElement =
    Interop.reactApi.createElement(swiperComp, createObj !!props)

let swiperSlide (props: IReactProperty list) : ReactElement =
    Interop.reactApi.createElement(swiperSlideComp, createObj !!props)

// Helpers pour les props
[<RequireQualifiedAccess>]
module Swiper =
    let slidesPerView (v: int) = prop.custom("slidesPerView", v)
    let spaceBetween (v: int) = prop.custom("spaceBetween", v)
    let onSlideChange (f: unit -> unit) = prop.custom("onSlideChange", f)
    let breakpoints (o: obj) = prop.custom("breakpoints", o)
    let children (xs: ReactElement list) = prop.children xs
