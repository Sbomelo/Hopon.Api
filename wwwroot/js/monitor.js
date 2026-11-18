(() => {
    const table = document.getElementById('trip-monitor');
    if (!table) return;
    const status = document.getElementById('monitor-status');
    let stopped = false;
    let timer;

    function cell(row, value) {
        const td = document.createElement('td');
        td.textContent = value ?? '—';
        row.append(td);
        return td;
    }

    async function refresh() {
        try {
            const response = await fetch(table.dataset.url, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' },
                signal: AbortSignal.timeout(10000)
            });
            if (response.redirected || response.status === 401 || response.status === 403) {
                stopped = true;
                throw new Error('Sign in again to continue monitoring.');
            }
            if (!response.ok) throw new Error('Update failed; showing the last successful snapshot. Retrying in 15 seconds.');
            const trips = await response.json();
            const body = document.createElement('tbody');
            for (const trip of trips) {
                const row = document.createElement('tr');
                cell(row, trip.route);
                cell(row, trip.bus);
                cell(row, trip.driver ?? 'Unassigned');
                cell(row, trip.status);
                const gps = cell(row, 'No GPS fix');
                if (trip.ageMinutes == null || trip.ageMinutes > 5) gps.className = 'text-error';
                if (trip.locationUrl) {
                    const link = document.createElement('a');
                    link.href = trip.locationUrl;
                    link.target = '_blank';
                    link.rel = 'noopener noreferrer';
                    link.textContent = `${trip.latitude.toFixed(5)}, ${trip.longitude.toFixed(5)} · ${trip.ageMinutes.toFixed(1)} min ago · OSM`;
                    gps.replaceChildren(link);
                }
                cell(row, trip.nextStop);
                cell(row, trip.finalEtaSast);
                const details = document.createElement('a');
                details.href = table.dataset.detailsUrl.replace(/\/0$/, `/${trip.tripId}`);
                details.textContent = 'Details';
                cell(row, '').append(details);
                body.append(row);
            }
            if (!trips.length) {
                const row = document.createElement('tr');
                cell(row, 'No in-progress or delayed trips.').colSpan = 8;
                body.append(row);
            }
            table.tBodies[0].replaceWith(body);
            status.textContent = `Updated ${new Date().toLocaleTimeString('en-ZA', { timeZone: 'Africa/Johannesburg' })} SAST.`;
            status.className = '';
        } catch (error) {
            status.textContent = error.message || 'Update failed; retrying in 15 seconds.';
            status.className = 'text-error';
        } finally {
            if (!stopped) timer = setTimeout(refresh, 15000);
        }
    }
    timer = setTimeout(refresh, 15000);
    window.addEventListener('pagehide', () => { stopped = true; clearTimeout(timer); });
})();
