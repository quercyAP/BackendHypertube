module Client.Components.Logo

open Feliz

// --- Dégradés communs ---
let private gradients (suffix: string) =
    Svg.defs [
        Svg.linearGradient [
            svg.id $"creamGradient-{suffix}"
            svg.x1 0
            svg.y1 0
            svg.x2 100
            svg.y2 100
            svg.children [
                Svg.stop [
                    svg.offset (length.percent 0)
                    svg.stopColor "#fae8d2"
                ]
                Svg.stop [
                    svg.offset (length.percent 100)
                    svg.stopColor "#f6d9b8"
                ]
            ]
        ]
        Svg.linearGradient [
            svg.id $"chocoGradient-{suffix}"
            svg.x1 0
            svg.y1 0
            svg.x2 0
            svg.y2 100
            svg.custom ("gradientUnits", "userSpaceOnUse")
            svg.children [
                Svg.stop [
                    svg.offset (length.percent 0)
                    svg.stopColor "#b77b52"
                ]
                Svg.stop [
                    svg.offset (length.percent 100)
                    svg.stopColor "#7a4a2a"
                ]
            ]
        ]
        Svg.radialGradient [
            svg.id $"coffeeSurface-{suffix}"
            svg.cx 50
            svg.cy 50
            svg.r 50
            svg.custom ("gradientUnits", "userSpaceOnUse")
            svg.children [
                Svg.stop [
                    svg.offset (length.percent 0)
                    svg.stopColor "#8b5a33"
                ]
                Svg.stop [
                    svg.offset (length.percent 100)
                    svg.stopColor "#5a341c"
                ]
            ]
        ]
    ]


// --- Composant tasse ☕️ ---
let cookieCreamCup (suffix: string) =
    Svg.svg [
        svg.xmlns "http://www.w3.org/2000/svg"
        svg.viewBox (0, 0, 70, 60)
        svg.width 70
        svg.height 60
        svg.children [
            gradients suffix

            Svg.g [
                svg.transform [ transform.translate(10, 10) ]
                svg.children [
                    Svg.ellipse [
                        svg.cx 25; svg.cy 40; svg.rx 28; svg.ry 8
                        svg.fill "#e6cbb0"
                    ]
                    Svg.path [
                        svg.d "M0,10 Q0,35 25,45 Q50,35 50,10 Z"
                        svg.fill $"url(#creamGradient-{suffix})"
                        svg.stroke "#7a4a2a"
                        svg.strokeWidth 3
                    ]
                    Svg.ellipse [
                        svg.cx 25; svg.cy 10; svg.rx 25; svg.ry 8
                        svg.fill $"url(#coffeeSurface-{suffix})"
                        svg.stroke "#7a4a2a"
                        svg.strokeWidth 2
                    ]
                    Svg.path [
                        svg.d "M12 7 Q17 4 22 7 Q27 10 32 7 Q37 4 40 7"
                        svg.fill "none"
                        svg.stroke "#f8e6cf"
                        svg.strokeWidth 1.5
                    ]
                    Svg.path [
                        svg.d "M10 11 Q15 8 20 11 Q25 14 30 11 Q35 8 38 11"
                        svg.fill "none"
                        svg.stroke "#f8e6cf"
                        svg.strokeWidth 1.3
                    ]
                    Svg.path [
                        svg.d "M50 18 Q63 25 50 35"
                        svg.fill "none"
                        svg.stroke "#7a4a2a"
                        svg.strokeWidth 4
                    ]
                ]
            ]
        ]
    ]

// --- Composant texte 🍪 ---
let cookieCreamText (suffix: string) =
    Svg.svg [
        svg.xmlns "http://www.w3.org/2000/svg"
        svg.viewBox (0, 0, 335, 90)
        svg.width 335
        svg.height 90

        svg.children [
            gradients suffix

            Svg.text [
                svg.x 0
                svg.y 65
                svg.custom ("fontFamily", "'Pacifico', cursive")
                svg.custom ("fontWeight", "400")
                svg.fontSize 60
                svg.fill "#5a341c"
                svg.stroke $"url(#creamGradient-{suffix})"
                svg.strokeWidth 6
                svg.custom ("paintOrder", "stroke")
                svg.text "CookieCream"
            ]

            Svg.text [
                svg.x 0
                svg.y 65
                svg.custom ("fontFamily", "'Pacifico', cursive")
                svg.fontSize 60
                svg.fill $"url(#chocoGradient-{suffix})"
                svg.stroke "#5a341c"
                svg.strokeWidth 2
                svg.custom ("paintOrder", "stroke")
                svg.text "CookieCream"
            ]
        ]
    ]


let cookieCreamLogo suffix =
    Svg.svg [
        svg.xmlns "http://www.w3.org/2000/svg"
        // même hauteur que le logo texte (90)
        svg.viewBox (0, 0, 425, 90)
        svg.width 390
        svg.height 90
        svg.children [
            gradients suffix

            // Tasse (abaissée légèrement pour aligner avec la baseline)
            Svg.g [
                svg.transform [ transform.translate(10, 25) ]
                svg.children [
                    Svg.ellipse [
                        svg.cx 25; svg.cy 40; svg.rx 28; svg.ry 8
                        svg.fill "#e6cbb0"
                    ]
                    Svg.path [
                        svg.d "M0,10 Q0,35 25,45 Q50,35 50,10 Z"
                        svg.fill $"url(#creamGradient-{suffix})"
                        svg.stroke "#7a4a2a"
                        svg.strokeWidth 3
                    ]
                    Svg.ellipse [
                        svg.cx 25; svg.cy 10; svg.rx 25; svg.ry 8
                        svg.fill $"url(#coffeeSurface-{suffix})"
                        svg.stroke "#7a4a2a"
                        svg.strokeWidth 2
                    ]
                    Svg.path [
                        svg.d "M12 7 Q17 4 22 7 Q27 10 32 7 Q37 4 40 7"
                        svg.fill "none"
                        svg.stroke "#f8e6cf"
                        svg.strokeWidth 1.5
                    ]
                    Svg.path [
                        svg.d "M10 11 Q15 8 20 11 Q25 14 30 11 Q35 8 38 11"
                        svg.fill "none"
                        svg.stroke "#f8e6cf"
                        svg.strokeWidth 1.3
                    ]
                    Svg.path [
                        svg.d "M50 18 Q63 25 50 35"
                        svg.fill "none"
                        svg.stroke "#7a4a2a"
                        svg.strokeWidth 4
                    ]
                ]
            ]

            // Texte (même style que cookieCreamText)
            Svg.text [
                svg.x 85
                svg.y 65
                svg.custom ("fontFamily", "'Pacifico', cursive")
                svg.custom ("fontWeight", "400")
                svg.fontSize 60
                svg.fill "#5a341c"
                svg.stroke $"url(#creamGradient-{suffix})"
                svg.strokeWidth 6
                svg.custom ("paintOrder", "stroke")
                svg.text "CookieCream"
            ]
            Svg.text [
                svg.x 85
                svg.y 65
                svg.custom ("fontFamily", "'Pacifico', cursive")
                svg.fontSize 60
                svg.fill $"url(#chocoGradient-{suffix})"
                svg.stroke "#5a341c"
                svg.strokeWidth 2
                svg.custom ("paintOrder", "stroke")
                svg.text "CookieCream"
            ]
        ]
    ]

let loginLogo =
    Svg.svg [
        svg.xmlns "http://www.w3.org/2000/svg"
        svg.viewBox (0, 0, 24, 24)
        svg.fill "none"
        svg.children [
            Svg.path [
                svg.d "M9 4.5H8C5.64298 4.5 4.46447 4.5 3.73223 5.23223C3 5.96447 3 7.14298 3 9.5V14.5C3 16.857 3 18.0355 3.73223 18.7678C4.46447 19.5 5.64298 19.5 8 19.5H9"
                svg.stroke "var(--primary, #FAE8D2)"
                svg.strokeWidth 1.5
            ]
            Svg.path [
                svg.d "M9 6.4764C9 4.18259 9 3.03569 9.70725 2.4087C10.4145 1.78171 11.4955 1.97026 13.6576 2.34736L15.9864 2.75354C18.3809 3.17118 19.5781 3.37999 20.2891 4.25826C21 5.13652 21 6.40672 21 8.94711V15.0529C21 17.5933 21 18.8635 20.2891 19.7417C19.5781 20.62 18.3809 20.8288 15.9864 21.2465L13.6576 21.6526C11.4955 22.0297 10.4145 22.2183 9.70725 21.5913C9 20.9643 9 19.8174 9 17.5236V6.4764Z"
                svg.stroke "var(--primary, #FAE8D2)"
                svg.strokeWidth 1.5
            ]
            Svg.path [
                svg.d "M12 11V13"
                svg.stroke "var(--accent, #E6C6A5)"
                svg.strokeWidth 1.5
                svg.custom ("strokeLinecap", "round")
            ]
        ]
    ]

let logoutLogo =
    Svg.svg [
        svg.xmlns "http://www.w3.org/2000/svg"
        svg.viewBox (0, 0, 24, 24)
        svg.fill "none"
        svg.children [
            // Porte / contour principal (même forme que logout)
            Svg.path [
                svg.d "M15 4.5H16C18.357 4.5 19.5355 4.5 20.2678 5.23223C21 5.96447 21 7.14298 21 9.5V14.5C21 16.857 21 18.0355 20.2678 18.7678C19.5355 19.5 18.357 19.5 16 19.5H15"
                svg.stroke "var(--primary, #FAE8D2)"
                svg.strokeWidth 1.5
            ]
            // Corps de la capsule / porte intérieure
            Svg.path [
                svg.d "M15 6.4764C15 4.18259 15 3.03569 14.2928 2.4087C13.5855 1.78171 12.5045 1.97026 10.3424 2.34736L8.01358 2.75354C5.61914 3.17118 4.42193 3.37999 3.71094 4.25826C3 5.13652 3 6.40672 3 8.94711V15.0529C3 17.5933 3 18.8635 3.71094 19.7417C4.42193 20.62 5.61914 20.8288 8.01358 21.2465L10.3424 21.6526C12.5045 22.0297 13.5855 22.2183 14.2928 21.5913C15 20.9643 15 19.8174 15 17.5236V6.4764Z"
                svg.stroke "var(--primary, #FAE8D2)"
                svg.strokeWidth 1.5
            ]
            // Flèche d'entrée (vers la gauche)
            Svg.path [
                svg.d "M12 12H7M7 12L9 10M7 12L9 14"
                svg.stroke "var(--accent, #E6C6A5)"
                svg.strokeWidth 1.5
                svg.custom ("strokeLinecap", "round")
            ]
        ]
    ]

let infoLogo isError =
    Svg.svg [
        svg.xmlns "http://www.w3.org/2000/svg"
        svg.viewBox (0, 0, 24, 24)
        svg.fill "none"
        svg.children [
            Svg.circle [
                svg.cx 12
                svg.cy 12
                svg.r 9
                if isError then svg.stroke "var(--error, #E74C3C)"
                else svg.stroke "var(--error, #FAE8D2)"
                svg.strokeWidth 1.5
            ]
            Svg.path [
                svg.d "M12 8V12"
                if isError then svg.stroke "var(--error-accent, #FF6B6B)"
                else svg.stroke "var(--accent, #E6C6A5)"
                svg.strokeWidth 1.5
                svg.custom ("strokeLinecap", "round")
            ]
            Svg.circle [
                svg.cx 12
                svg.cy 16
                svg.r 1
                if isError then svg.fill "var(--error-accent, #FF6B6B)"
                else svg.fill "var(--accent, #E6C6A5)"
            ]
        ]
    ]
