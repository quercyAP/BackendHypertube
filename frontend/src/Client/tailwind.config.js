/** @type {import('tailwindcss').Config} */
module.exports = {
    mode: "jit",
    content: [
        "./index.html",
        "./**/*.{fs,js,ts,jsx,tsx}",
        "!./node_modules/**/*",
    ],
    theme: {
        extend: {
            // colors: {
            //     "primary": "#FAE8D2",
            //     "secondary": "#A58967",
            //     "dark-primary": "#331F06",
            //     "dark-secondary": "#503617",
            //     "accent": "#7A5D38",
            //     "light": "#FAE8D2",
            //     "dark": "#331F06",
            //     "error": "#dc3545",
            //
            //     "text-primary": "#FAE8D2",
            //     "text-secondary": "#503617",
            //     "text-accent": "#331F06",
            // },
            colors: {
                "primary": "#FAE8D2",          // crème douce (texte principal)
                "primary-light": "#FFF4E6",    // version plus claire (hover)
                "secondary": "#A58967",        // caramel
                "secondary-dark": "#7A5D38",   // moka profond (hover)
                "dark-primary": "#2B1A05",     // chocolat noir (fond global)
                "dark-secondary": "#4A2E0F",   // brun moyen (fenêtres)
                "accent": "#E6C6A5",           // touche latte / pastel
                "highlight": "#FFD8A8",        // effet glow subtil
                "error-accent": "#FF6B6B",     // rouge corail vif
                "error": "#E74C3C",            // rouge tomate élégant
                "text-primary": "#FAE8D2",     // clair sur fond sombre
                "text-secondary": "#E6C6A5",
                "text-accent": "#A58967",
            },

            fontFamily: {
                pacifico: ['"Pacifico"', 'cursive'],
            },
        },
    },
    plugins: []
}
