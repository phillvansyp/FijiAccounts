const fileDropRegistrations = new WeakMap();

function unregisterFileDrop(element) {
    const registration = fileDropRegistrations.get(element);

    if (!registration) {
        return;
    }

    element.removeEventListener("dragenter", registration.dragOver);
    element.removeEventListener("dragover", registration.dragOver);
    element.removeEventListener("dragleave", registration.dragLeave);
    element.removeEventListener("drop", registration.drop);
    element.classList.remove("drag-over");
    fileDropRegistrations.delete(element);
}

window.fileDrop = {
    register: function (element, inputId) {
        if (!element) {
            return;
        }

        unregisterFileDrop(element);

        const dragOver = function (event) {
            event.preventDefault();
            event.dataTransfer.dropEffect = "copy";
            element.classList.add("drag-over");
        };

        const dragLeave = function (event) {
            if (!element.contains(event.relatedTarget)) {
                element.classList.remove("drag-over");
            }
        };

        const drop = function (event) {
            event.preventDefault();
            event.stopPropagation();
            element.classList.remove("drag-over");

            const input = document.getElementById(inputId);

            if (!input || !event.dataTransfer || event.dataTransfer.files.length === 0) {
                return;
            }

            const dataTransfer = new DataTransfer();

            for (const file of event.dataTransfer.files) {
                dataTransfer.items.add(file);
            }

            input.files = dataTransfer.files;
            input.dispatchEvent(new Event("change", { bubbles: true }));
        };

        element.addEventListener("dragenter", dragOver);
        element.addEventListener("dragover", dragOver);
        element.addEventListener("dragleave", dragLeave);
        element.addEventListener("drop", drop);

        fileDropRegistrations.set(element, {
            dragOver,
            dragLeave,
            drop
        });
    },

    unregister: function (element) {
        unregisterFileDrop(element);
    }
};
