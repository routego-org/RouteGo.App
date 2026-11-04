// The operator dashboard's two sections each reload on their own: "Show figures" asks the portal
// for the sales section alone and "Show trips" for the trips section alone, so choosing one never
// loads the other from Reporting. The page works without this script: both forms then reload the
// whole page. Every word shown comes from the server; the section HTML is the portal's own answer.
(function () {
    'use strict';

    var root = document.querySelector('[data-dashboard]');
    if (!root) {
        return;
    }

    var holders = {
        sales: root.querySelector('[data-sales-holder]'),
        trips: root.querySelector('[data-trips-holder]')
    };
    var addresses = {
        sales: root.getAttribute('data-sales-address'),
        trips: root.getAttribute('data-trips-address')
    };
    var pageAddress = root.getAttribute('data-page-address');

    function field(form, name) {
        return form.querySelector('[name="' + name + '"]');
    }

    function forms() {
        return {
            sales: holders.sales.querySelector('form'),
            trips: holders.trips.querySelector('form')
        };
    }

    // The section that was not redrawn keeps its own copy of the other days; bring those up to date.
    function syncHidden() {
        var both = forms();
        field(both.sales, 'day').value = field(both.trips, 'day').value;
        field(both.trips, 'from').value = field(both.sales, 'from').value;
        field(both.trips, 'to').value = field(both.sales, 'to').value;
    }

    function allDays() {
        var both = forms();
        var days = new URLSearchParams();
        days.set('from', field(both.sales, 'from').value);
        days.set('to', field(both.sales, 'to').value);
        days.set('day', field(both.trips, 'day').value);
        return days;
    }

    function sectionAddress(kind, form) {
        var query = new URLSearchParams(new FormData(form));
        return addresses[kind] + '&' + query.toString();
    }

    async function reload(kind, form) {
        var response = await fetch(sectionAddress(kind, form), { credentials: 'same-origin' });
        if (!response.ok) {
            throw new Error('section not drawn');
        }
        holders[kind].innerHTML = (await response.text()).trim();
        syncHidden();
        window.history.replaceState(null, '', pageAddress.split('?')[0] + '?' + allDays().toString());
    }

    document.addEventListener('submit', function (event) {
        var form = event.target;
        var kind = form.getAttribute && form.getAttribute('data-section-form');
        if (!kind || !holders[kind]) {
            return;
        }
        event.preventDefault();
        reload(kind, form).catch(function () {
            // Reporting or the portal could not answer the section: reload the whole page so the
            // person sees the same message they would without the script.
            var query = new URLSearchParams(new FormData(form)).toString();
            window.location.assign(pageAddress.split('?')[0] + '?' + query);
        });
    });
}());
