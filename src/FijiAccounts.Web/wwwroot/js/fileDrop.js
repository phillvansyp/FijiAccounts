window.fileDrop = {

    register: function (element, inputId) {

        if (!element) {
            return;
        }

        element.addEventListener("dragover", function (event) {
            event.preventDefault();
            element.classList.add("drag-over");
        });

        element.addEventListener("dragleave", function () {
            element.classList.remove("drag-over");
        });

        element.addEventListener("drop", function (event) {

            event.preventDefault();

            element.classList.remove("drag-over");

            const input =
                document.getElementById(inputId);

            if (!input) {
                return;
            }

            const dataTransfer =
                new DataTransfer();

            for (const file of event.dataTransfer.files) {
                dataTransfer.items.add(file);
            }

            input.files =
                dataTransfer.files;

            input.dispatchEvent(
                new Event(
                    "change",
                    {
                        bubbles: true
                    }
                )
            );
        });
    }
};
