// The hold countdown: counts the seconds left on an unpaid booking down in words, every second, and
// loads the page again once when it reaches zero so the page shows what Booking says then. The
// seconds come from the server as loaded, so a wrong clock in the browser changes nothing.
(function () {
    var element = document.querySelector('[data-hold-countdown]');
    if (!element) {
        return;
    }

    var label = element.querySelector('[data-time-left]');
    var secondsLeft = parseInt(element.getAttribute('data-seconds-left'), 10);
    if (!label || !(secondsLeft > 0)) {
        return;
    }

    function counted(count, unit) {
        return count === 1 ? '1 ' + unit : count + ' ' + unit + 's';
    }

    var timer = window.setInterval(function () {
        secondsLeft -= 1;
        if (secondsLeft <= 0) {
            window.clearInterval(timer);
            window.location.reload();
            return;
        }

        label.textContent = counted(Math.floor(secondsLeft / 60), 'minute') + ' ' + counted(secondsLeft % 60, 'second');
    }, 1000);
})();
