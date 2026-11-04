// Bus photos: each chosen file goes straight from this browser to RouteGo's photo storage, never
// through the portal. For each file, in the order chosen, the portal asks Fleet for an upload slot,
// this script sends the file to it, and the portal asks Fleet to check and add the photo. The portal
// makes every Fleet call and writes every word; this script only checks the file first, sends it,
// and shows the answers. Without JavaScript the "Add photos" panel stays hidden.
(function () {
    'use strict';

    var JPEG_START = [0xFF, 0xD8, 0xFF];
    var PNG_START = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    var ASK_AGAIN_AFTER_MS = 2000;

    function showUploadPanels() {
        document.querySelectorAll('[data-upload-form]').forEach(function (form) {
            form.hidden = false;
        });
    }

    function fill(template, values) {
        return template.replace(/\{(\w+)\}/g, function (whole, key) {
            if (Object.prototype.hasOwnProperty.call(values, key)) {
                return String(values[key]);
            }
            return whole;
        });
    }

    function startsWith(bytes, signature) {
        if (bytes.length < signature.length) {
            return false;
        }
        for (var index = 0; index < signature.length; index++) {
            if (bytes[index] !== signature[index]) {
                return false;
            }
        }
        return true;
    }

    // The type comes from the file's first bytes, not its name.
    async function formatOf(file) {
        var head = new Uint8Array(await file.slice(0, PNG_START.length).arrayBuffer());
        if (startsWith(head, JPEG_START)) {
            return 'jpeg';
        }
        if (startsWith(head, PNG_START)) {
            return 'png';
        }
        return null;
    }

    function wait(milliseconds) {
        return new Promise(function (resolve) {
            window.setTimeout(resolve, milliseconds);
        });
    }

    // One step through the portal. A redirect means the session or the form ended: follow it.
    async function postStep(form, address, fields) {
        var body = new FormData();
        body.append('__RequestVerificationToken', form.querySelector('input[name="__RequestVerificationToken"]').value);
        Object.keys(fields).forEach(function (name) {
            body.append(name, fields[name]);
        });
        var response = await fetch(address, { method: 'POST', body: body, credentials: 'same-origin' });
        if (response.redirected) {
            window.location.assign(response.url);
            throw new Error('redirected');
        }
        if (!response.ok) {
            throw new Error('the portal answered ' + response.status);
        }
        return response.json();
    }

    // The file itself, to photo storage. No cookie goes with it.
    async function sendFile(step, file) {
        try {
            var response = await fetch(step.uploadUrl, {
                method: step.uploadMethod,
                headers: { 'Content-Type': step.contentType },
                body: file,
                credentials: 'omit'
            });
            return response.ok;
        } catch (error) {
            return false;
        }
    }

    async function addFile(form, file, line) {
        var words = form.dataset;
        var name = { file_name: file.name };
        var format = await formatOf(file);
        if (format === null) {
            return { message: fill(words.wordWrongType, name) };
        }
        if (file.size > parseInt(words.maxBytes, 10)) {
            return { message: fill(words.wordTooLarge, name) };
        }

        line.textContent = file.name + ' — ' + words.wordSending;
        var slot = await postStep(form, words.slotAddress, { fileName: file.name, format: format, sizeBytes: file.size });
        if (slot.outcome !== 'send') {
            return slot;
        }

        var isSent = await sendFile(slot, file);
        if (!isSent) {
            return postStep(form, words.discardAddress, { photoId: slot.photoId, fileName: file.name });
        }

        var confirm = { photoId: slot.photoId, fileName: file.name, attempt: 1 };
        var answer = await postStep(form, words.confirmAddress, confirm);
        if (answer.outcome === 'askAgain') {
            await wait(ASK_AGAIN_AFTER_MS);
            confirm.attempt = 2;
            answer = await postStep(form, words.confirmAddress, confirm);
        }
        return answer;
    }

    async function addFileSafely(form, file, line) {
        try {
            return await addFile(form, file, line);
        } catch (error) {
            // A network failure reads as "could not be sent"; anything else as our own failure.
            var template = form.dataset.wordServerError;
            if (error instanceof TypeError) {
                template = form.dataset.wordNotSent;
            }
            return { message: fill(template, { file_name: file.name }) };
        }
    }

    async function drawSectionAgain(address) {
        var response = await fetch(address, { credentials: 'same-origin' });
        if (response.redirected) {
            window.location.assign(response.url);
            return;
        }
        if (!response.ok) {
            return;
        }
        var holder = document.createElement('template');
        holder.innerHTML = (await response.text()).trim();
        var fresh = holder.content.querySelector('[data-photo-section]');
        var current = document.querySelector('[data-photo-section]');
        if (fresh !== null && current !== null) {
            current.replaceWith(fresh);
            showUploadPanels();
        }
    }

    function setBusy(form, isBusy) {
        form.querySelector('[data-photo-files]').disabled = isBusy;
        var button = form.querySelector('[data-add-photos]');
        button.classList.toggle('disabled', isBusy);
        button.setAttribute('aria-disabled', String(isBusy));
    }

    function listFiles(files, waitingWord) {
        var list = document.querySelector('[data-upload-results]');
        list.replaceChildren();
        return files.map(function (file) {
            var line = document.createElement('li');
            line.setAttribute('data-upload-result', '');
            line.textContent = file.name + ' — ' + waitingWord;
            list.appendChild(line);
            return line;
        });
    }

    async function addFiles(form, files) {
        var lines = listFiles(files, form.dataset.wordWaiting);
        for (var index = 0; index < files.length; index++) {
            var step = await addFileSafely(form, files[index], lines[index]);
            lines[index].textContent = step.message;
            if (step.stops) {
                // The account cannot add photos now: the files not yet tried are not sent at all.
                lines.slice(index + 1).forEach(function (line) {
                    line.remove();
                });
                break;
            }
        }
    }

    document.addEventListener('change', async function (event) {
        var input = event.target;
        if (!input.matches('[data-photo-files]')) {
            return;
        }
        var form = input.closest('[data-upload-form]');
        var files = Array.from(input.files);
        input.value = '';
        if (files.length === 0) {
            return;
        }

        var refusal = form.querySelector('[data-upload-refusal]');
        refusal.textContent = '';
        var free = parseInt(form.dataset.free, 10);
        if (files.length > free) {
            var template = free === 1 ? form.dataset.wordTooManyOne : form.dataset.wordTooMany;
            refusal.textContent = fill(template, { free: free, n: files.length });
            return;
        }

        setBusy(form, true);
        try {
            await addFiles(form, files);
        } finally {
            await drawSectionAgain(form.dataset.sectionAddress);
            setBusy(form, false);
        }
    });

    showUploadPanels();
})();
