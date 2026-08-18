(function () {
    var STORAGE_KEY = "bl-theme";

    function getPreferredTheme() {
        var stored = null;
        try {
            stored = localStorage.getItem(STORAGE_KEY);
        } catch (e) { /* localStorage unavailable */ }

        if (stored === "light" || stored === "dark") {
            return stored;
        }

        return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches
            ? "dark"
            : "light";
    }

    function updateIcons(theme) {
        var icons = document.querySelectorAll("[data-theme-icon]");
        for (var i = 0; i < icons.length; i++) {
            icons[i].textContent = theme === "dark" ? "☀️" : "🌙";
        }
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute("data-theme", theme);
        updateIcons(theme);
    }

    applyTheme(getPreferredTheme());

    window.toggleTheme = function () {
        var current = document.documentElement.getAttribute("data-theme") === "dark" ? "dark" : "light";
        var next = current === "dark" ? "light" : "dark";
        try {
            localStorage.setItem(STORAGE_KEY, next);
        } catch (e) { /* localStorage unavailable */ }
        applyTheme(next);
    };

    function reapply() {
        applyTheme(getPreferredTheme());
    }

    document.addEventListener("DOMContentLoaded", reapply);
    // Blazor's enhanced navigation swaps <body> content (and can reset
    // attributes on <html>) without re-running this script, so the theme
    // has to be reapplied after every enhanced-nav page transition too.
    document.addEventListener("enhancedload", reapply);

    // Belt-and-braces: some Blazor Server navigation/reconnect paths clear
    // the data-theme attribute without firing any of the events above.
    // Watch the attribute directly and restore it whenever that happens.
    if (window.MutationObserver && document.documentElement) {
        new MutationObserver(function () {
            var current = document.documentElement.getAttribute("data-theme");
            if (current !== "light" && current !== "dark") {
                applyTheme(getPreferredTheme());
            }
        }).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    }
})();
