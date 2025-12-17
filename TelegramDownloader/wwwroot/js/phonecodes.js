iti = null;
function loadCountries() {
    const input = document.querySelector("#phone");
    if (iti === null)
        iti = window.intlTelInput(input, {
            utilsScript: "https://cdn.jsdelivr.net/npm/intl-tel-input@23.0.12/build/js/utils.js",
        });
}

function getNumber() {
    return iti.getNumber();
}

function focusElement(elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.focus();
        // Also select all text if it's an input
        if (element.select) {
            element.select();
        }
    }
}