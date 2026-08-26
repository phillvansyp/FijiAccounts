(() => {
    const countdown = document.getElementById("lockout-countdown");
    if (!countdown) {
        return;
    }

    const seconds = Number.parseInt(countdown.dataset.seconds ?? "60", 10);
    const endsAt = Date.now() + Math.max(1, seconds) * 1000;

    const update = () => {
        const remaining = Math.max(0, Math.ceil((endsAt - Date.now()) / 1000));
        const minutes = Math.floor(remaining / 60).toString().padStart(2, "0");
        const displaySeconds = (remaining % 60).toString().padStart(2, "0");
        countdown.textContent = `${minutes}:${displaySeconds}`;

        if (remaining === 0) {
            window.clearInterval(timer);
            const status = document.getElementById("lockout-status");
            if (status) {
                status.textContent = "You can sign in again now.";
            }

            window.location.replace(countdown.dataset.loginUrl ?? "/Account/Login");
        }
    };

    const timer = window.setInterval(update, 250);
    update();
})();
