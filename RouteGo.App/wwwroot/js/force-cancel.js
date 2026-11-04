// The force-cancel question names the refund ("in full" or "50% of ₹1,099"). As the percentage is
// typed, the words follow it; the server writes the same words when the page loads, so without this
// script they simply stay as loaded. The amount is only ever the booking's total: the refund amount
// itself is Booking's to work out.
(function () {
    var card = document.querySelector('[data-force-cancel-question]');
    var question = card && card.querySelector('[data-question]');
    var box = card && card.querySelector('[name="Input.RefundPercent"]');
    if (!card || !question || !box) {
        return;
    }

    var total = parseInt(card.getAttribute('data-total'), 10);
    var pattern = /(refunded )(in full|\d+% of ₹[\d,]+)(\. This cannot be undone\.)$/;

    function refundWords() {
        var typed = box.value.trim();
        if (typed === '' || !/^[+-]?\d+$/.test(typed)) {
            return 'in full';
        }

        var percent = parseInt(typed, 10);
        if (percent < 0 || percent > 100) {
            return 'in full';
        }

        return percent + '% of ₹' + total.toLocaleString('en-US');
    }

    box.addEventListener('input', function () {
        question.textContent = question.textContent.replace(pattern, '$1' + refundWords() + '$3');
    });
})();
