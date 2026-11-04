// No double submit (shared rule 5): while a form is being submitted its submit button cannot be
// chosen again. The page works the same without JavaScript; this only blocks the second click.
document.addEventListener('submit', function (event) {
    var form = event.target;
    if (form.dataset.submitting === 'true') {
        event.preventDefault();
        return;
    }
    form.dataset.submitting = 'true';
    // Disabled a moment later: the browser collects the form's fields after this event, and a
    // disabled button would drop the pressed button's own name and value (Move earlier, Fill...).
    window.setTimeout(function () {
        form.querySelectorAll('button[type="submit"]').forEach(function (button) {
            button.disabled = true;
        });
    }, 0);
});

// A page restored from the back/forward cache must be usable again.
window.addEventListener('pageshow', function () {
    document.querySelectorAll('form[data-submitting="true"]').forEach(function (form) {
        form.dataset.submitting = 'false';
        form.querySelectorAll('button[type="submit"]').forEach(function (button) {
            button.disabled = false;
        });
    });
});

// A button with data-wait-seconds stays disabled for that long (asked-too-often states).
document.querySelectorAll('[data-wait-seconds]').forEach(function (button) {
    var seconds = parseInt(button.getAttribute('data-wait-seconds'), 10);
    if (seconds > 0) {
        button.disabled = true;
        window.setTimeout(function () { button.disabled = false; }, seconds * 1000);
    }
});
