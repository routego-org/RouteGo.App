// Book seats: ticking a seat opens that seat's passenger boxes and updates the total and the Pay
// button; unticking closes them. At most 6 seats can be ticked. Without this script the page still
// works: "Update seats" posts the ticks and the server draws the boxes and the total. Text is only
// ever written with textContent, from numbers read off the page.
(function () {
    var form = document.querySelector('[data-book-form]');
    if (!form) {
        return;
    }

    var maxSeats = 6;
    var tooMany = 'You can book up to 6 seats.';
    var boxes = Array.prototype.slice.call(form.querySelectorAll('[data-pick]'));
    var total = form.querySelector('[data-total]');
    var pay = form.querySelector('[data-pay]');
    var hint = form.querySelector('[data-choose-hint]');
    var update = form.querySelector('[data-update-seats]');
    var notice = document.createElement('div');
    notice.className = 'alert alert-danger';
    notice.setAttribute('role', 'alert');
    notice.hidden = true;
    form.insertBefore(notice, form.firstChild);

    if (update) {
        update.hidden = true;
    }

    function rupees(amount) {
        return '₹' + String(amount).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    }

    function sync() {
        var sum = 0;
        var chosen = 0;
        boxes.forEach(function (box) {
            var passenger = form.querySelector('[data-passenger="' + box.value + '"]');
            if (passenger) {
                passenger.hidden = !box.checked;
                passenger.disabled = !box.checked;
            }

            if (box.checked) {
                chosen += 1;
                sum += parseInt(box.getAttribute('data-amount'), 10);
            }
        });

        total.textContent = 'Total ' + rupees(sum);
        pay.textContent = chosen === 0 ? 'Pay' : 'Pay ' + rupees(sum);
        if (hint) {
            hint.hidden = chosen > 0;
        }
    }

    boxes.forEach(function (box) {
        box.addEventListener('change', function () {
            var chosen = boxes.filter(function (other) { return other.checked; }).length;
            if (box.checked && chosen > maxSeats) {
                box.checked = false;
                notice.textContent = tooMany;
                notice.hidden = false;
            } else {
                notice.hidden = true;
            }

            sync();
        });
    });

    sync();
})();
