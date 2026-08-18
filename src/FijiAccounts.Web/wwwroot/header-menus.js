document.addEventListener("toggle", event => {
    const openedMenu = event.target;

    if (!(openedMenu instanceof HTMLDetailsElement) ||
        !openedMenu.open ||
        !openedMenu.closest(".app-header")) {
        return;
    }

    document.querySelectorAll(".app-header details[open]").forEach(menu => {
        if (menu !== openedMenu) {
            menu.open = false;
        }
    });
}, true);

document.addEventListener("click", event => {
    if (event.target.closest(".app-header details")) {
        return;
    }

    document.querySelectorAll(".app-header details[open]").forEach(menu => {
        menu.open = false;
    });
});

document.addEventListener("keydown", event => {
    if (event.key !== "Escape") {
        return;
    }

    document.querySelectorAll(".app-header details[open]").forEach(menu => {
        menu.open = false;
    });
});
