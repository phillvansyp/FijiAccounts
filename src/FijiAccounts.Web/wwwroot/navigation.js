window.accountIslandNavigation = {
    saveBankView(view) {
        const url = new URL(window.location.href);
        if (!url.pathname.endsWith("/banking")) return;
        if (url.searchParams.get("bankView") === view) return;
        url.searchParams.set("bankView", view);
        window.history.replaceState(window.history.state, "", url);
    },
    savePage(page) {
        const url = new URL(window.location.href);
        if (!url.pathname.endsWith("/report-transactions")) return;
        if (url.searchParams.get("page") === String(page)) return;
        url.searchParams.set("page", page);
        window.history.replaceState(window.history.state, "", url);
    },
    saveView(view) {
        const url = new URL(window.location.href);
        if (!/\/reports(?:\/[^/]+)?$/.test(url.pathname)) return;
        if (url.searchParams.get("view") === view) return;
        url.searchParams.set("view", view);
        // Preserve Blazor's navigation metadata for browser Back and Forward.
        window.history.replaceState(window.history.state, "", url);
    }
};
