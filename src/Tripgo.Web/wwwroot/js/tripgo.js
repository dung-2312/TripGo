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

    initMap: function (containerId, mapData) {
        var self = this;
        if (typeof L === 'undefined') {
            console.warn("Leaflet library not ready, retrying in 200ms...");
            setTimeout(function () { self.initMap(containerId, mapData); }, 200);
            return;
        }

        var container = document.getElementById(containerId);
        if (!container) return;

        // Cleanup existing map if any
        if (self._map) {
            self._map.remove();
            self._map = null;
            self._markers = {};
        }

        var coords = mapData.polylineCoordinates || [];
        var pickup = mapData.selectedPickupPoint;
        var dropoff = mapData.selectedDropoffPoint;
        var restStops = mapData.restStops || [];

        // Center initially on pickup or Vietnam center
        var initialCoord = (pickup && pickup.latitude && pickup.longitude)
            ? [pickup.latitude, pickup.longitude]
            : (coords.length > 0 ? coords[0] : [16.0544, 108.2022]);

        // Create Leaflet Map with controls
        self._map = L.map(containerId, {
            zoomControl: false,
            attributionControl: true
        }).setView(initialCoord, 9);

        L.control.zoom({ position: 'topright' }).addTo(self._map);

        // OpenStreetMap tile layer with standard attribution
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a> contributors'
        }).addTo(self._map);

        // Render Road Route Polyline if coords available
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

            // Fit bounds with generous padding for mobile & desktop
            try {
                self._map.fitBounds(self._polyline.getBounds(), { padding: [40, 40] });
            } catch (e) {
                console.warn("Could not fit bounds:", e);
            }
        }

        // Add Pickup Marker (Green)
        if (pickup && pickup.latitude && pickup.longitude) {
            var pickupIcon = L.divIcon({
                className: 'tripgo-marker-pickup',
                html: '<div style="background:#16a34a;color:white;width:34px;height:34px;border-radius:50%;display:flex;align-items:center;justify-content:center;box-shadow:0 0 10px rgba(22,163,74,0.6);border:2px solid white;font-size:16px;"><i class="bi bi-box-arrow-in-right"></i></div>',
                iconSize: [34, 34],
                iconAnchor: [17, 17]
            });
            var pMarker = L.marker([pickup.latitude, pickup.longitude], { icon: pickupIcon, zIndexOffset: 500 }).addTo(self._map);
            var pPopup = '<div style="min-width:220px;font-family:system-ui,-apple-system,sans-serif;">' +
                '<div style="font-size:11px;font-weight:700;color:#16a34a;text-transform:uppercase;margin-bottom:3px;"><i class="bi bi-geo-alt-fill me-1"></i>Điểm đón đã chọn</div>' +
                '<div style="font-size:14px;font-weight:bold;color:#1e293b;margin-bottom:3px;">' + (pickup.name || 'Điểm đón') + '</div>' +
                '<div style="font-size:12px;color:#64748b;margin-bottom:6px;">' + (pickup.address || '') + '</div>' +
                (pickup.scheduledTime ? '<div style="font-size:12px;background:#f0fdf4;color:#166534;padding:4px 8px;border-radius:6px;border:1px solid #bbf7d0;"><strong>Giờ đón:</strong> ' + pickup.scheduledTime + '</div>' : '') +
                '</div>';
            pMarker.bindPopup(pPopup);
            self._markers['pickup'] = pMarker;
        }

        // Add Dropoff Marker (Red)
        if (dropoff && dropoff.latitude && dropoff.longitude) {
            var dropoffIcon = L.divIcon({
                className: 'tripgo-marker-dropoff',
                html: '<div style="background:#dc2626;color:white;width:34px;height:34px;border-radius:50%;display:flex;align-items:center;justify-content:center;box-shadow:0 0 10px rgba(220,38,38,0.6);border:2px solid white;font-size:16px;"><i class="bi bi-flag-fill"></i></div>',
                iconSize: [34, 34],
                iconAnchor: [17, 17]
            });
            var dMarker = L.marker([dropoff.latitude, dropoff.longitude], { icon: dropoffIcon, zIndexOffset: 500 }).addTo(self._map);
            var dPopup = '<div style="min-width:220px;font-family:system-ui,-apple-system,sans-serif;">' +
                '<div style="font-size:11px;font-weight:700;color:#dc2626;text-transform:uppercase;margin-bottom:3px;"><i class="bi bi-geo-alt-fill me-1"></i>Điểm trả đã chọn</div>' +
                '<div style="font-size:14px;font-weight:bold;color:#1e293b;margin-bottom:3px;">' + (dropoff.name || 'Điểm trả') + '</div>' +
                '<div style="font-size:12px;color:#64748b;margin-bottom:6px;">' + (dropoff.address || '') + '</div>' +
                (dropoff.scheduledTime ? '<div style="font-size:12px;background:#fef2f2;color:#991b1b;padding:4px 8px;border-radius:6px;border:1px solid #fecaca;"><strong>Giờ trả dự kiến:</strong> ' + dropoff.scheduledTime + '</div>' : '') +
                '</div>';
            dMarker.bindPopup(dPopup);
            self._markers['dropoff'] = dMarker;
        }

        // Add Verified Rest Stops Markers (Amber)
        restStops.forEach(function (stop) {
            if (!stop.latitude || !stop.longitude) return;
            var stopIcon = L.divIcon({
                className: 'tripgo-marker-reststop',
                html: '<div style="background:#f59e0b;color:white;width:32px;height:32px;border-radius:50%;display:flex;align-items:center;justify-content:center;box-shadow:0 0 8px rgba(245,158,11,0.6);border:2px solid white;font-size:15px;"><i class="bi bi-cup-hot-fill"></i></div>',
                iconSize: [32, 32],
                iconAnchor: [16, 16]
            });
            var sMarker = L.marker([stop.latitude, stop.longitude], { icon: stopIcon, zIndexOffset: 400 }).addTo(self._map);
            var sPopup = '<div style="min-width:240px;font-family:system-ui,-apple-system,sans-serif;">' +
                '<div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:4px;">' +
                '<span style="font-size:11px;font-weight:700;color:#d97706;text-transform:uppercase;"><i class="bi bi-cup-hot me-1"></i>Trạm dừng chân</span>' +
                (stop.stayMinutes ? '<span style="font-size:11px;background:#fef3c7;color:#92400e;padding:2px 6px;border-radius:4px;font-weight:600;">Nghỉ ~' + stop.stayMinutes + ' phút</span>' : '') +
                '</div>' +
                '<div style="font-size:14px;font-weight:bold;color:#1e293b;margin-bottom:3px;">' + stop.name + '</div>' +
                '<div style="font-size:12px;color:#64748b;margin-bottom:6px;">' + (stop.address || '') + '</div>' +
                '<div style="font-size:11px;color:#047857;background:#ecfdf5;padding:4px 8px;border-radius:4px;border:1px solid #a7f3d0;margin-bottom:6px;">' +
                '<i class="bi bi-shield-check me-1"></i>' + (stop.verificationStatus || 'Xác nhận bởi nhà xe') +
                '</div>' +
                (stop.services ? '<div style="font-size:11px;color:#475569;"><strong>Tiện ích:</strong> ' + stop.services + '</div>' : '') +
                '</div>';
            sMarker.bindPopup(sPopup);
            self._markers['stop_' + stop.id] = sMarker;
        });

        // Ensure proper rendering when modal animation completes
        setTimeout(function () {
            if (self._map) {
                self._map.invalidateSize();
                if (self._polyline) {
                    try {
                        self._map.fitBounds(self._polyline.getBounds(), { padding: [40, 40] });
                    } catch (e) { }
                }
            }
        }, 300);
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
        if (this._map) {
            this._map.remove();
            this._map = null;
            this._markers = {};
        }
    }
};
