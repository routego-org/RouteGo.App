// Change seats: a seat picked as the new seat for one of the booking's seats is left out of the
// other seats' choices while the person picks. The page leaves the same seat out when it is drawn
// again after a refusal, and the server checks the moves either way, so this only saves a round trip.
(function () {
    var selects = Array.prototype.slice.call(document.querySelectorAll('[data-new-seat]'));
    if (selects.length < 2) {
        return;
    }

    function update() {
        var taken = selects.map(function (select) { return select.value; });
        selects.forEach(function (select, index) {
            Array.prototype.forEach.call(select.options, function (option) {
                var isTakenByAnother = option.value !== '' && taken.some(function (value, other) {
                    return other !== index && value === option.value;
                });
                option.disabled = isTakenByAnother;
                option.hidden = isTakenByAnother;
            });
        });
    }

    selects.forEach(function (select) { select.addEventListener('change', update); });
    update();
})();
