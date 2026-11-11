window.tripgo = {
    scrollToResults: function () {
        document.getElementById("trip-results")?.scrollIntoView({ behavior: "smooth", block: "start" });
    },
    goBack: function (fallbackUrl) {
        var origin = window.location.origin;
        var hasInternalReferrer = document.referrer && document.referrer.indexOf(origin) === 0;
        if (hasInternalReferrer && window.history && window.history.length > 1) {
            window.history.back();
        } else {
            window.location.href = fallbackUrl || "/";
        }
    }
};

window.tripgoMap = {
    _map: null,
    _polyline: null,
    _markers: {},
    _resizeObserver: null,
    _retryTimers: {},

    initMap: function (containerId, mapData) {
        var self = this;
        if (!containerId || !mapData) return;

        // If Leaflet library is not yet loaded, retry up to 30 times (3 seconds)
        if (typeof L === 'undefined') {
            console.warn("[TripGo Map] Leaflet library not ready, retrying in 100ms...");
            setTimeout(function () { self.initMap(containerId, mapData); }, 100);
            return;
        }

        var container = document.getElementById(containerId);
        if (!container) {
            console.warn("[TripGo Map] Container #" + containerId + " not found in DOM, retrying in 100ms...");
            if (!self._retryTimers[containerId]) self._retryTimers[containerId] = 0;
            self._retryTimers[containerId]++;
            if (self._retryTimers[containerId] <= 30) {
                setTimeout(function () { self.initMap(containerId, mapData); }, 100);
            }
            return;
        }
        self._retryTimers[containerId] = 0;

        // Cleanup previous map instance
        self.destroy();

        // Clear Leaflet internal id from DOM element to avoid "Map container is already initialized"
        if (container._leaflet_id) {
            container._leaflet_id = null;
        }

        // Support both camelCase and PascalCase
        var coords = mapData.polylineCoordinates || mapData.PolylineCoordinates || [];
        var pickup = mapData.selectedPickupPoint || mapData.SelectedPickupPoint || {};
        var dropoff = mapData.selectedDropoffPoint || mapData.SelectedDropoffPoint || {};
        var restStops = mapData.restStops || mapData.RestStops || [];

        var pLat = pickup.latitude != null ? pickup.latitude : pickup.Latitude;
        var pLng = pickup.longitude != null ? pickup.longitude : pickup.Longitude;
        var pName = pickup.name || pickup.Name || 'Điểm đón';
        var pAddress = pickup.address || pickup.Address || '';
        var pTime = pickup.scheduledTime || pickup.ScheduledTime || '';

        var dLat = dropoff.latitude != null ? dropoff.latitude : dropoff.Latitude;
        var dLng = dropoff.longitude != null ? dropoff.longitude : dropoff.Longitude;
        var dName = dropoff.name || dropoff.Name || 'Điểm trả';
        var dAddress = dropoff.address || dropoff.Address || '';
        var dTime = dropoff.scheduledTime || dropoff.ScheduledTime || '';

        // Default initial center (Pickup, or first coord, or Vietnam center)
        var initialCoord = (pLat && pLng) ? [pLat, pLng] : (coords.length > 0 ? coords[0] : [21.0285, 105.8542]);

        try {
            self._map = L.map(containerId, {
                zoomControl: false,
                attributionControl: true
            }).setView(initialCoord, 8);
        } catch (err) {
            console.error("[TripGo Map] Failed to create L.map:", err);
            return;
        }

        L.control.zoom({ position: 'topright' }).addTo(self._map);

        // OpenStreetMap tile layer
        L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a> contributors'
        }).addTo(self._map);

        // Render Road Route Polyline if coordinates available
        if (coords.length >= 2) {
            // Subtle glowing underlayer
            L.polyline(coords, {
                color: '#3b82f6',
                weight: 8,
                opacity: 0.35,
                lineCap: 'round',
                lineJoin: 'round'
            }).addTo(self._map);

            // Main road route polyline
            self._polyline = L.polyline(coords, {
                color: '#1d4ed8',
                weight: 5,
                opacity: 0.95,
                lineCap: 'round',
                lineJoin: 'round'
            }).addTo(self._map);
        }

        // Add Pickup Marker (Green)
        if (pLat && pLng) {
            var pickupIcon = L.divIcon({
                className: 'tripgo-marker-pickup',
                html: '<div style="background:#16a34a;color:white;width:34px;height:34px;border-radius:50%;display:flex;align-items:center;justify-content:center;box-shadow:0 0 10px rgba(22,163,74,0.6);border:2px solid white;font-size:16px;"><i class="bi bi-box-arrow-in-right"></i></div>',
                iconSize: [34, 34],
                iconAnchor: [17, 17]
            });
            var pMarker = L.marker([pLat, pLng], { icon: pickupIcon, zIndexOffset: 500 }).addTo(self._map);
            var pPopup = '<div style="min-width:220px;font-family:system-ui,-apple-system,sans-serif;">' +
                '<div style="font-size:11px;font-weight:700;color:#16a34a;text-transform:uppercase;margin-bottom:3px;"><i class="bi bi-geo-alt-fill me-1"></i>Điểm đón đã chọn</div>' +
                '<div style="font-size:14px;font-weight:bold;color:#1e293b;margin-bottom:3px;">' + pName + '</div>' +
                '<div style="font-size:12px;color:#64748b;margin-bottom:6px;">' + pAddress + '</div>' +
                (pTime ? '<div style="font-size:12px;background:#f0fdf4;color:#166534;padding:4px 8px;border-radius:6px;border:1px solid #bbf7d0;"><strong>Giờ đón:</strong> ' + pTime + '</div>' : '') +
                '</div>';
            pMarker.bindPopup(pPopup);
            self._markers['pickup'] = pMarker;
        }

        // Add Dropoff Marker (Red)
        if (dLat && dLng) {
            var dropoffIcon = L.divIcon({
                className: 'tripgo-marker-dropoff',
                html: '<div style="background:#dc2626;color:white;width:34px;height:34px;border-radius:50%;display:flex;align-items:center;justify-content:center;box-shadow:0 0 10px rgba(220,38,38,0.6);border:2px solid white;font-size:16px;"><i class="bi bi-flag-fill"></i></div>',
                iconSize: [34, 34],
                iconAnchor: [17, 17]
            });
            var dMarker = L.marker([dLat, dLng], { icon: dropoffIcon, zIndexOffset: 500 }).addTo(self._map);
            var dPopup = '<div style="min-width:220px;font-family:system-ui,-apple-system,sans-serif;">' +
                '<div style="font-size:11px;font-weight:700;color:#dc2626;text-transform:uppercase;margin-bottom:3px;"><i class="bi bi-geo-alt-fill me-1"></i>Điểm trả đã chọn</div>' +
                '<div style="font-size:14px;font-weight:bold;color:#1e293b;margin-bottom:3px;">' + dName + '</div>' +
                '<div style="font-size:12px;color:#64748b;margin-bottom:6px;">' + dAddress + '</div>' +
                (dTime ? '<div style="font-size:12px;background:#fef2f2;color:#991b1b;padding:4px 8px;border-radius:6px;border:1px solid #fecaca;"><strong>Giờ trả dự kiến:</strong> ' + dTime + '</div>' : '') +
                '</div>';
            dMarker.bindPopup(dPopup);
            self._markers['dropoff'] = dMarker;
        }

        // Add Rest Stops (Amber)
        restStops.forEach(function (stop) {
            var sLat = stop.latitude != null ? stop.latitude : stop.Latitude;
            var sLng = stop.longitude != null ? stop.longitude : stop.Longitude;
            if (!sLat || !sLng) return;
            var sId = stop.id != null ? stop.id : stop.Id;
            var sName = stop.name || stop.Name || 'Trạm dừng chân';
            var sAddress = stop.address || stop.Address || '';
            var sStay = stop.stayMinutes != null ? stop.stayMinutes : stop.StayMinutes;
            var sStatus = stop.verificationStatus || stop.VerificationStatus || 'Xác nhận bởi nhà xe';
            var sServices = stop.services || stop.Services || '';

            var stopIcon = L.divIcon({
                className: 'tripgo-marker-reststop',
                html: '<div style="background:#f59e0b;color:white;width:32px;height:32px;border-radius:50%;display:flex;align-items:center;justify-content:center;box-shadow:0 0 8px rgba(245,158,11,0.6);border:2px solid white;font-size:15px;"><i class="bi bi-cup-hot-fill"></i></div>',
                iconSize: [32, 32],
                iconAnchor: [16, 16]
            });
            var sMarker = L.marker([sLat, sLng], { icon: stopIcon, zIndexOffset: 400 }).addTo(self._map);
            var sPopup = '<div style="min-width:240px;font-family:system-ui,-apple-system,sans-serif;">' +
                '<div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:4px;">' +
                '<span style="font-size:11px;font-weight:700;color:#d97706;text-transform:uppercase;"><i class="bi bi-cup-hot me-1"></i>Trạm dừng chân</span>' +
                (sStay ? '<span style="font-size:11px;background:#fef3c7;color:#92400e;padding:2px 6px;border-radius:4px;font-weight:600;">Nghỉ ~' + sStay + ' phút</span>' : '') +
                '</div>' +
                '<div style="font-size:14px;font-weight:bold;color:#1e293b;margin-bottom:3px;">' + sName + '</div>' +
                '<div style="font-size:12px;color:#64748b;margin-bottom:6px;">' + sAddress + '</div>' +
                '<div style="font-size:11px;color:#047857;background:#ecfdf5;padding:4px 8px;border-radius:4px;border:1px solid #a7f3d0;margin-bottom:6px;">' +
                '<i class="bi bi-shield-check me-1"></i>' + sStatus +
                '</div>' +
                (sServices ? '<div style="font-size:11px;color:#475569;"><strong>Tiện ích:</strong> ' + sServices + '</div>' : '') +
                '</div>';
            sMarker.bindPopup(sPopup);
            self._markers['stop_' + sId] = sMarker;
        });

        // Smart fit bounds
        function doFitBounds() {
            if (!self._map) return;
            try {
                self._map.invalidateSize();
                if (self._polyline) {
                    self._map.fitBounds(self._polyline.getBounds(), { padding: [40, 40] });
                } else if (pLat && pLng && dLat && dLng) {
                    var bounds = L.latLngBounds([[pLat, pLng], [dLat, dLng]]);
                    self._map.fitBounds(bounds, { padding: [50, 50] });
                }
            } catch (e) { }
        }

        // Trigger size adjustment at multiple intervals to handle modal slide/fade animation
        doFitBounds();
        setTimeout(doFitBounds, 100);
        setTimeout(doFitBounds, 300);
        setTimeout(doFitBounds, 600);
        setTimeout(doFitBounds, 1200);

        // ResizeObserver for automatic resize handling if user resizes window or modal
        if (window.ResizeObserver && container) {
            try {
                self._resizeObserver = new ResizeObserver(function () {
                    if (self._map) {
                        self._map.invalidateSize();
                    }
                });
                self._resizeObserver.observe(container);
            } catch (e) { }
        }
    },

    fitRoute: function () {
        if (this._map && this._polyline) {
            try {
                this._map.fitBounds(this._polyline.getBounds(), { padding: [40, 40] });
            } catch (e) { }
        }
    },

    focusPoint: function (lat, lng, name, key) {
        if (!this._map) return;
        this._map.flyTo([lat, lng], 13, { duration: 1.0 });
        if (key && this._markers[key]) {
            this._markers[key].openPopup();
        }
    },

    destroy: function () {
        if (this._resizeObserver) {
            try { this._resizeObserver.disconnect(); } catch (e) { }
            this._resizeObserver = null;
        }
        if (this._map) {
            try {
                this._map.remove();
            } catch (e) { }
            this._map = null;
            this._polyline = null;
            this._markers = {};
        }
    }
};
