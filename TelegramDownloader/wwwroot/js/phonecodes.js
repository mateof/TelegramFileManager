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