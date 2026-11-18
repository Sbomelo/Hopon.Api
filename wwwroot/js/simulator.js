(() => {
    const panel = document.getElementById('sim-panel');
    if (!panel) return;
    const auto = document.getElementById('sim-auto');
    const message = document.getElementById('sim-message');
    let timer;
    let busy = false;
    let closed = false;

    function schedule() {
        clearTimeout(timer);
        auto.disabled = panel.querySelector('.sim-state').dataset.auto !== 'yes';
        if (auto.disabled || closed) auto.checked = false;
        if (auto.checked) timer = setTimeout(() => act('auto'), 3000);
    }

    async function act(command) {
        if (busy || closed) return;
        clearTimeout(timer);
        busy = true;
        const form = panel.querySelector('form');
        const data = new FormData(form);
        data.set('command', command);
        form.setAttribute('inert', '');
        try {
            const response = await fetch(form.action, {
                method: 'POST', body: data, headers: { 'X-Simulator-Panel': '1' }
            });
            if (!response.ok || response.redirected) throw new Error('Refresh the simulator to reconnect.');
            const html = await response.text();
            if (closed) return;
            const draft = form.elements.reason.value;
            panel.innerHTML = html;
            if (command !== 'delay' || panel.querySelector('[role="alert"]')) panel.querySelector('#reason').value = draft;
            if (panel.querySelector('[role="alert"]')) auto.checked = false;
            message.textContent = '';
        } catch (err) {
            auto.checked = false;
            message.textContent = err.message;
        } finally {
            form.removeAttribute('inert');
            busy = false;
            schedule();
        }
    }

    panel.addEventListener('submit', (event) => {
        event.preventDefault();
        if (event.submitter) act(event.submitter.value);
    });
    auto.addEventListener('change', schedule);
    panel.addEventListener('focusin', (event) => {
        if (event.target.id === 'reason' && auto.checked) {
            auto.checked = false;
            schedule();
        }
    });
    window.addEventListener('pagehide', () => { closed = true; auto.checked = false; clearTimeout(timer); });
    window.addEventListener('pageshow', () => { closed = false; schedule(); });
    schedule();
})();
