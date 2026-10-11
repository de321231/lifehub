/* Frühe Theme-Anwendung, damit beim Laden kein Farbwechsel aufblitzt.
   Bewusst als eigene Datei, weil die Content-Security-Policy keine Inline-Skripte erlaubt. */
(function () {
    try {
        var stored = localStorage.getItem("desd-lifehub-theme");
        if (stored === "light" || stored === "dark") {
            document.documentElement.setAttribute("data-theme", stored);
        }
    } catch (e) {
        /* localStorage nicht verfügbar – Systemdarstellung greift. */
    }
})();
