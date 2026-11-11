using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tripgo.Application.Interfaces;

namespace Tripgo.Infrastructure.Services;

public class OsrmRoadRoutingService : IRoadRoutingService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache? _cache;
    private readonly ILogger<OsrmRoadRoutingService>? _logger;
    private readonly string _baseUrl;
    private readonly int _timeoutSeconds;

    public OsrmRoadRoutingService(
        HttpClient httpClient,
        IConfiguration? configuration = null,
        IMemoryCache? cache = null,
        ILogger<OsrmRoadRoutingService>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache;
        _logger = logger;

        _baseUrl = (configuration?["RoadRouting:BaseUrl"] ?? configuration?["RoadRouting:OsrmBaseUrl"] ?? "https://router.project-osrm.org").TrimEnd('/');
        if (!int.TryParse(configuration?["RoadRouting:TimeoutSeconds"], out _timeoutSeconds) || _timeoutSeconds <= 0)
        {
            _timeoutSeconds = 6;
        }
    }

    public async Task<RoadRoutingResult> GetDrivingRouteAsync(
        double originLat,
        double originLng,
        double destLat,
        double destLng,
        CancellationToken ct = default)
    {
        // 1. Validate coordinate ranges
        if (originLat < -90.0 || originLat > 90.0 || originLng < -180.0 || originLng > 180.0 ||
            destLat < -90.0 || destLat > 90.0 || destLng < -180.0 || destLng > 180.0)
        {
            return new RoadRoutingResult
            {
                Success = false,
                ErrorMessage = "Tọa độ điểm đón hoặc điểm trả không hợp lệ (-90 đến 90 cho vĩ độ, -180 đến 180 cho kinh độ)."
            };
        }

        // 2. Reject zero or near-identical origin and destination
        if (Math.Abs(originLat - destLat) < 0.0001 && Math.Abs(originLng - destLng) < 0.0001)
        {
            return new RoadRoutingResult
            {
                Success = false,
                ErrorMessage = "Điểm đón và điểm trả quá gần nhau hoặc trùng một vị trí."
            };
        }

        // 3. Cache lookup
        var cacheKey = string.Create(CultureInfo.InvariantCulture, $"osrm_route_{originLat:F4}_{originLng:F4}_{destLat:F4}_{destLng:F4}");
        if (_cache != null && _cache.TryGetValue(cacheKey, out RoadRoutingResult? cachedResult) && cachedResult != null)
        {
            _logger?.LogDebug("Returning cached road route for key {CacheKey}", cacheKey);
            return cachedResult;
        }

        // 4. Construct OSRM driving API request (OSRM expects {longitude},{latitude};{longitude},{latitude})
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{_baseUrl}/route/v1/driving/{originLng:F6},{originLat:F6};{destLng:F6},{destLat:F6}?overview=full&geometries=geojson");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "TripGo-RoadRouting/1.0 (BusTicketPlatform)");

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("OSRM request failed with HTTP status {StatusCode} for URL {Url}", response.StatusCode, url);
                return new RoadRoutingResult
                {
                    Success = false,
                    ErrorMessage = $"Máy chủ định tuyến đường bộ phản hồi mã lỗi HTTP {(int)response.StatusCode}."
                };
            }

            var jsonStream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cts.Token);
            var root = doc.RootElement;

            if (!root.TryGetProperty("code", out var codeElement) || codeElement.GetString() != "Ok")
            {
                var codeStr = codeElement.ValueKind == JsonValueKind.String ? codeElement.GetString() : "Unknown";
                _logger?.LogWarning("OSRM returned non-OK status: {Code}", codeStr);
                return new RoadRoutingResult
                {
                    Success = false,
                    ErrorMessage = codeStr == "NoRoute"
                        ? "Không tìm thấy tuyến đường bộ hợp lệ kết nối giữa hai địa điểm này."
                        : $"Máy chủ định tuyến phản hồi trạng thái: {codeStr}"
                };
            }

            if (!root.TryGetProperty("routes", out var routesElement) || routesElement.GetArrayLength() == 0)
            {
                return new RoadRoutingResult
                {
                    Success = false,
                    ErrorMessage = "Không tìm thấy dữ liệu đường đi chi tiết từ nhà cung cấp bản đồ."
                };
            }

            var primaryRoute = routesElement[0];
            var distanceMeters = primaryRoute.TryGetProperty("distance", out var distProp) ? distProp.GetDouble() : 0.0;
            var durationSeconds = primaryRoute.TryGetProperty("duration", out var durProp) ? durProp.GetDouble() : 0.0;

            var geometryPoints = new List<double[]>();
            if (primaryRoute.TryGetProperty("geometry", out var geomProp) &&
                geomProp.TryGetProperty("coordinates", out var coordsProp) &&
                coordsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var coord in coordsProp.EnumerateArray())
                {
                    if (coord.ValueKind == JsonValueKind.Array && coord.GetArrayLength() >= 2)
                    {
                        var lon = coord[0].GetDouble();
                        var lat = coord[1].GetDouble();
                        // Leaflet uses [latitude, longitude]
                        geometryPoints.Add(new[] { Math.Round(lat, 6), Math.Round(lon, 6) });
                    }
                }
            }

            if (geometryPoints.Count < 2)
            {
                return new RoadRoutingResult
                {
                    Success = false,
                    ErrorMessage = "Dữ liệu hình học đường bộ (geometry) không đầy đủ."
                };
            }

            var result = new RoadRoutingResult
            {
                Success = true,
                Geometry = geometryPoints,
                DistanceMeters = distanceMeters,
                DurationSeconds = durationSeconds,
                ProviderSource = "OSRM Road Network (OpenStreetMap)"
            };

            // Cache successful result for 24 hours
            if (_cache != null)
            {
                _cache.Set(cacheKey, result, TimeSpan.FromHours(24));
            }

            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger?.LogWarning("OSRM request timed out after {Timeout} seconds for {Url}", _timeoutSeconds, url);
            return new RoadRoutingResult
            {
                Success = false,
                ErrorMessage = "Dịch vụ định tuyến đường bộ phản hồi quá thời gian quy định (Timeout). Vui lòng thử lại sau."
            };
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "Network error during OSRM call to {Url}", url);
            return new RoadRoutingResult
            {
                Success = false,
                ErrorMessage = "Không thể kết nối đến máy chủ định tuyến đường bộ OSRM."
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error in OsrmRoadRoutingService for {Url}", url);
            return new RoadRoutingResult
            {
                Success = false,
                ErrorMessage = $"Lỗi xử lý dữ liệu lộ trình đường bộ: {ex.Message}"
            };
        }
    }
}

