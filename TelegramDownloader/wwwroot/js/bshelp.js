var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'))
var tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
    return new bootstrap.Tooltip(tooltipTriggerEl)
})

function copyToClipboard(text) {
    navigator.clipboard.writeText(text);
}

function focusElement (id) {
    const element = document.getElementById(id);
    element.focus();
}
