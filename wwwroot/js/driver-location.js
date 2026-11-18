(() => {
    const form = document.getElementById('gps-form');
    if (!form) return;
    const toggle = document.getElementById('gps-auto');
    const stopButton = document.getElementById('gps-stop');
    const indicator = document.getElementById('gps-indicator');
    const status = document.getElementById('gps-status');
    const button = document.getElementById('gps-btn');
    let watch = null, timer = null, latest = null, busy = false, controller = null;
    // Devices share every 5–10 seconds; Hopon uses 10 seconds and starts off.
    function stop() {
        if (watch !== null) navigator.geolocation.clearWatch(watch);
        clearInterval(timer);
        controller?.abort();
        watch = timer = latest = null;
        toggle.checked = false;
        indicator.hidden = stopButton.hidden = true;
    }
    async function send(position) {
        if (busy || Date.now() - position.timestamp > 30000) return;
        busy = true;
        controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 15000);
        try {
            const data = new FormData(form);
            data.set('Latitude', position.coords.latitude);
            data.set('Longitude', position.coords.longitude);
            const response = await fetch(form.action, { method: 'POST', body: data,
                headers: { 'X-Requested-With': 'fetch' }, signal: controller.signal });
            if (response.redirected) throw new Error('Please sign in again.');
            const result = await response.json();
            status.textContent = result.message;
            if (!response.ok && response.status !== 429) stop();
        } catch (error) {
            if (error.name !== 'AbortError') { status.textContent = 'Could not share location. Check your connection and sign-in.'; stop(); }
        } finally { clearTimeout(timeout); busy = false; }
    }
    function failure(error) { stop(); status.textContent = 'Could not get location: ' + error.message; }
    toggle.addEventListener('change', () => {
        if (!toggle.checked) { stop(); return; }
        if (!navigator.geolocation) { failure({ message: 'Geolocation is unavailable.' }); return; }
        indicator.hidden = stopButton.hidden = false;
        status.textContent = 'Waiting for GPS…';
        watch = navigator.geolocation.watchPosition(p => { latest = p; }, failure,
            { enableHighAccuracy: true, timeout: 15000, maximumAge: 0 });
        timer = setInterval(() => { if (latest) send(latest); }, 10000);
    });
    stopButton.addEventListener('click', () => { stop(); status.textContent = 'Location sharing stopped.'; });
    button.addEventListener('click', () => {
        if (!navigator.geolocation) { failure({ message: 'Geolocation is unavailable.' }); return; }
        navigator.geolocation.getCurrentPosition(send, failure, { enableHighAccuracy: true, timeout: 10000, maximumAge: 0 });
    });
    window.addEventListener('pagehide', stop);
})();
