(function () {
    let deferredPrompt = null;

    function isInstalled() {
        return window.matchMedia("(display-mode: standalone)").matches
            || window.navigator.standalone === true;
    }

    function syncInstallButtons() {
        const canInstall = deferredPrompt !== null && !isInstalled();
        document.querySelectorAll("[data-install-app]").forEach(button => {
            button.hidden = !canInstall;
            button.disabled = !canInstall;
        });
    }

    async function promptForInstall() {
        if (!deferredPrompt || isInstalled()) {
            syncInstallButtons();
            return false;
        }

        const promptEvent = deferredPrompt;
        deferredPrompt = null;
        syncInstallButtons();
        await promptEvent.prompt();
        const choice = await promptEvent.userChoice;
        syncInstallButtons();
        return choice.outcome === "accepted";
    }

    window.accountIslandInstall = { prompt: promptForInstall };

    window.addEventListener("beforeinstallprompt", event => {
        event.preventDefault();
        deferredPrompt = event;
        syncInstallButtons();
    });

    window.addEventListener("appinstalled", () => {
        deferredPrompt = null;
        syncInstallButtons();
    });

    function initialise() {
        syncInstallButtons();
        if ("serviceWorker" in navigator) {
            navigator.serviceWorker.register("/service-worker.js").catch(error => {
                console.warn("Account Island offline support could not be started.", error);
            });
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialise, { once: true });
    } else {
        initialise();
    }

    window.addEventListener("pageshow", syncInstallButtons);
})();
