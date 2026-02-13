// refreshArrivals.js
// Shared script for arrivals refresh (BatchIndex & Index)

function refreshArrivals(options) {
    const {
        basePath,
        arrivalsSelector,
        timestampSelector,
        stopNameSelector,
        apiType,
        stopCodes,
        count
    } = options;

    async function update() {
        try {
            let url;
            if (apiType === 'batch') {
                url = `${basePath}/Home?`;
                (stopCodes || []).forEach(code => {
                    url += `stopCode=${encodeURIComponent(code)}&`;
                });
                if (count) url += `count=${count}&`;
                url += 'partial=true';

                const response = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
                const parser = new DOMParser();
                const htmlDoc = parser.parseFromString(await response.text(), 'text/html');
                const arrivals = htmlDoc.querySelector(arrivalsSelector);
                const arrivalsContainer = document.querySelector(arrivalsSelector);
                if (arrivals && arrivalsContainer) arrivalsContainer.innerHTML = arrivals.innerHTML;
            } else {
                // single stop
                const stopCode = stopCodes && stopCodes.length > 0 ? stopCodes[0] : '10606';
                url = `${basePath}/api/arrivals/${stopCode}`;
                if (count) url += `?count=${count}`;
                const response = await fetch(url);
                if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
                const data = await response.json();
                const arrivalsContainer = document.querySelector(arrivalsSelector);
                if (arrivalsContainer && data.arrivals && data.arrivals.length > 0) {
                    // Clear existing content
                    arrivalsContainer.innerHTML = '';

                    // Header row
                    const headerRow = document.createElement('div');
                    headerRow.className = 'arrival-row';
                    headerRow.style.fontWeight = 'bold';
                    headerRow.style.textTransform = 'uppercase';
                    headerRow.style.color = 'white';
                    headerRow.style.background = 'black';
                    headerRow.style.fontFamily = 'Segoe UI, Roboto, Arial, sans-serif';

                    const headerLine = document.createElement('div');
                    headerLine.className = 'line';
                    headerLine.textContent = 'LINIA';
                    headerRow.appendChild(headerLine);

                    const headerDirection = document.createElement('div');
                    headerDirection.className = 'direction';
                    headerDirection.textContent = 'KIERUNEK';
                    headerRow.appendChild(headerDirection);

                    const headerEtaPlanned = document.createElement('div');
                    headerEtaPlanned.className = 'eta';
                    headerEtaPlanned.textContent = 'PLANOWO';
                    headerRow.appendChild(headerEtaPlanned);

                    const headerEtaDeparture = document.createElement('div');
                    headerEtaDeparture.className = 'eta';
                    headerEtaDeparture.textContent = 'ODJAZD';
                    headerRow.appendChild(headerEtaDeparture);

                    arrivalsContainer.appendChild(headerRow);

                    // Arrival rows
                    data.arrivals.forEach(arrival => {
                        const row = document.createElement('div');
                        row.className = 'arrival-row ' + (arrival.isRealTime ? 'rt' : 'tt');

                        const lineDiv = document.createElement('div');
                        lineDiv.className = 'line';
                        lineDiv.textContent = arrival.line != null ? String(arrival.line) : '';
                        row.appendChild(lineDiv);

                        const directionDiv = document.createElement('div');
                        directionDiv.className = 'direction';
                        directionDiv.textContent = arrival.direction != null ? String(arrival.direction) : '';
                        row.appendChild(directionDiv);

                        const etaPlannedDiv = document.createElement('div');
                        etaPlannedDiv.className = 'eta';
                        etaPlannedDiv.textContent = arrival.scheduledDeparture
                            ? arrival.scheduledDeparture.substring(0, 5)
                            : '';
                        row.appendChild(etaPlannedDiv);

                        const etaDiv = document.createElement('div');
                        etaDiv.className = 'eta';
                        etaDiv.textContent = (arrival.etaMin != null ? String(arrival.etaMin) : '') + ' min';
                        row.appendChild(etaDiv);

                        arrivalsContainer.appendChild(row);
                    });
                } else if (arrivalsContainer) {
                    arrivalsContainer.innerHTML = '';
                    const emptyDiv = document.createElement('div');
                    emptyDiv.className = 'empty';
                    emptyDiv.textContent = 'Brak nadchodzących odjazdów.';
                    arrivalsContainer.appendChild(emptyDiv);
                }
                if (stopNameSelector && data.stopName && data.stopCode) {
                    const stopNameEl = document.querySelector(stopNameSelector);
                    if (stopNameEl) stopNameEl.textContent = `${data.stopName.toUpperCase()} (${data.stopCode})`;
                }
            }
            // update timestamp
            const timestampEl = document.querySelector(timestampSelector);
            if (timestampEl) {
                const now = new Date();
                const dateStr = now.toLocaleDateString('pl-PL', {
                    year: 'numeric', month: '2-digit', day: '2-digit'
                });
                const timeStr = now.toLocaleTimeString('pl-PL', {
                    hour: '2-digit', minute: '2-digit', second: '2-digit'
                });
                timestampEl.textContent = `${dateStr} ${timeStr}`;
            }
        } catch (error) {
            console.error('Błąd podczas pobierania danych:', error);
        }
    }

    setInterval(update, 30000);
    document.addEventListener('DOMContentLoaded', function() {
        const timestampEl = document.querySelector(timestampSelector);
        if (timestampEl) {
            timestampEl.style.cursor = 'pointer';
            timestampEl.title = 'Kliknij aby odświeżyć dane';
            timestampEl.addEventListener('click', update);
        }
    });
    // Initial call
    update();
}
