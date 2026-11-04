// City suggestions for the bus search form (home page and /bus-search). Typing in From or To asks
// the portal (which asks Search) for matching cities; picking one fills the box and its hidden
// identifier and name. The form works without this script: a city typed but not picked comes back
// from "Search" with the matching cities to choose from. Every word shown comes from the server.
(function () {
    'use strict';

    var WAIT_MS = 250;

    document.querySelectorAll('[data-search-form]').forEach(function (form) {
        var citiesUrl = form.getAttribute('data-cities-url');
        var unavailableText = form.getAttribute('data-unavailable-text');
        var fields = {};
        form.querySelectorAll('[data-city-field]').forEach(function (element) {
            fields[element.getAttribute('data-city-field')] = cityField(element);
        });

        function cityField(element) {
            return {
                role: element.getAttribute('data-city-field'),
                text: element.querySelector('[data-city-text]'),
                id: element.querySelector('[data-city-id]'),
                name: element.querySelector('[data-city-name]'),
                list: element.querySelector('[data-city-list]'),
                note: element.querySelector('[data-city-note]'),
                timer: 0,
                asked: 0,
                active: -1
            };
        }

        function clearPick(field) {
            field.id.value = '';
            field.name.value = '';
        }

        function closeList(field) {
            field.list.hidden = true;
            field.list.textContent = '';
            field.active = -1;
            field.text.setAttribute('aria-expanded', 'false');
            field.text.removeAttribute('aria-activedescendant');
        }

        function showNote(field, text) {
            field.note.textContent = text || '';
        }

        function pick(field, city) {
            var changedFrom = field.role === 'from' && field.id.value !== city.cityId;
            field.text.value = city.name;
            field.id.value = city.cityId;
            field.name.value = city.name;
            closeList(field);
            showNote(field, '');
            // Changing From empties To: the To suggestions follow the From city.
            if (changedFrom && fields.to) {
                fields.to.text.value = '';
                clearPick(fields.to);
                closeList(fields.to);
                showNote(fields.to, '');
            }
        }

        function showCities(field, cities) {
            closeList(field);
            cities.forEach(function (city, index) {
                var option = document.createElement('li');
                option.id = field.list.id + '-' + index;
                option.className = 'city-option';
                option.setAttribute('role', 'option');
                option.textContent = city.name;
                // mousedown, not click: the box's blur would close the list before a click lands.
                option.addEventListener('mousedown', function (event) {
                    event.preventDefault();
                    pick(field, city);
                });
                field.list.appendChild(option);
            });
            field.cities = cities;
            field.list.hidden = cities.length === 0;
            field.text.setAttribute('aria-expanded', cities.length > 0 ? 'true' : 'false');
        }

        function ask(field) {
            var typed = field.text.value.trim();
            if (typed.length === 0) {
                closeList(field);
                showNote(field, '');
                return;
            }

            var query = '&prefix=' + encodeURIComponent(typed) + '&cityRole=' + field.role;
            if (field.role === 'to' && fields.from && fields.from.id.value) {
                query += '&fromCityId=' + encodeURIComponent(fields.from.id.value);
            }

            // Only the newest answer is shown: an older one may arrive after it.
            field.asked += 1;
            var number = field.asked;
            fetch(citiesUrl + query, { headers: { 'Accept': 'application/json' } })
                .then(function (response) {
                    if (!response.ok) {
                        throw new Error('cities ' + response.status);
                    }
                    return response.json();
                })
                .then(function (answer) {
                    if (number !== field.asked) {
                        return;
                    }
                    showCities(field, answer.items || []);
                    showNote(field, answer.note);
                })
                .catch(function () {
                    if (number !== field.asked) {
                        return;
                    }
                    closeList(field);
                    showNote(field, unavailableText);
                });
        }

        function moveActive(field, step) {
            var options = field.list.querySelectorAll('[role="option"]');
            if (options.length === 0) {
                return;
            }
            field.active = (field.active + step + options.length) % options.length;
            options.forEach(function (option, index) {
                option.classList.toggle('active', index === field.active);
                option.setAttribute('aria-selected', index === field.active ? 'true' : 'false');
            });
            field.text.setAttribute('aria-activedescendant', options[field.active].id);
        }

        Object.keys(fields).forEach(function (role) {
            var field = fields[role];
            field.text.addEventListener('input', function () {
                clearPick(field);
                window.clearTimeout(field.timer);
                field.timer = window.setTimeout(function () { ask(field); }, WAIT_MS);
            });
            field.text.addEventListener('keydown', function (event) {
                if (field.list.hidden) {
                    return;
                }
                if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                    event.preventDefault();
                    moveActive(field, event.key === 'ArrowDown' ? 1 : -1);
                } else if (event.key === 'Enter' && field.active >= 0) {
                    event.preventDefault();
                    pick(field, field.cities[field.active]);
                } else if (event.key === 'Escape') {
                    closeList(field);
                }
            });
            field.text.addEventListener('blur', function () {
                closeList(field);
            });
        });
    });
})();
