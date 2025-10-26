using System.Diagnostics.Metrics;

namespace mylogging.observability.Common
{
    /// <summary>
    /// Provides methods for recording metrics such as counters, histograms, gauges, and up-down counters.
    /// </summary>
    public interface IMetricsService
    {
        /// <summary>
        /// Increments a counter metric by the specified value.
        /// </summary>
        /// <param name="name">The name of the counter metric.</param>
        /// <param name="value">The value to increment by. Default is 1.</param>
        /// <param name="tags">Optional tags to attach to the metric.</param>
        void IncrementCounter(string name, double value = 1, params KeyValuePair<string, object?>[] tags);

        /// <summary>
        /// Records a value in a histogram metric.
        /// </summary>
        /// <param name="name">The name of the histogram metric.</param>
        /// <param name="value">The value to record.</param>
        /// <param name="tags">Optional tags to attach to the metric.</param>
        void RecordHistogram(string name, double value, params KeyValuePair<string, object?>[] tags);

        /// <summary>
        /// Sets the current value of a gauge metric.
        /// </summary>
        /// <param name="name">The name of the gauge metric.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="tags">Optional tags to attach to the metric.</param>
        void SetGauge(string name, double value, params KeyValuePair<string, object?>[] tags);

        /// <summary>
        /// Increments or decrements an up-down counter metric by the specified value.
        /// </summary>
        /// <param name="name">The name of the up-down counter metric.</param>
        /// <param name="value">The value to increment or decrement by. Default is 1.</param>
        /// <param name="tags">Optional tags to attach to the metric.</param>
        void IncrementUpDownCounter(string name, double value = 1, params KeyValuePair<string, object?>[] tags);

        /// <summary>
        /// Records the duration of an HTTP request.
        /// </summary>
        /// <param name="durationMs">The duration of the request in milliseconds.</param>
        /// <param name="method">The HTTP method (e.g., GET, POST).</param>
        /// <param name="endpoint">The endpoint or route of the request.</param>
        /// <param name="statusCode">The HTTP status code of the response.</param>
        void RecordRequestDuration(double durationMs, string method, string endpoint, int statusCode);

        /// <summary>
        /// Increments the count of HTTP requests.
        /// </summary>
        /// <param name="method">The HTTP method (e.g., GET, POST).</param>
        /// <param name="endpoint">The endpoint or route of the request.</param>
        /// <param name="statusCode">The HTTP status code of the response.</param>
        void IncrementRequestCount(string method, string endpoint, int statusCode);

        /// <summary>
        /// Records an error occurrence.
        /// </summary>
        /// <param name="errorType">The type of error that occurred.</param>
        /// <param name="operation">The operation during which the error occurred.</param>
        void RecordErrorCount(string errorType, string operation);

        /// <summary>
        /// Sets the number of active connections.
        /// </summary>
        /// <param name="count">The current number of active connections.</param>
        void SetActiveConnections(int count);

        /// <summary>
        /// Records the duration of a database query.
        /// </summary>
        /// <param name="durationMs">The duration of the query in milliseconds.</param>
        /// <param name="operation">The database operation (e.g., SELECT, INSERT).</param>
        /// <param name="table">The database table being queried.</param>
        void RecordDatabaseQueryDuration(double durationMs, string operation, string table);

        /// <summary>
        /// Increments the count of active HTTP requests.
        /// </summary>
        void IncrementActiveRequests();

        /// <summary>
        /// Decrements the count of active HTTP requests.
        /// </summary>
        void DecrementActiveRequests();
    }

    /// <summary>
    /// Implementation of <see cref="IMetricsService"/> for recording application metrics.
    /// </summary>
    public class MetricsService : IMetricsService
    {
        private readonly Meter _meter;
        private readonly ObservabilityOptions _options;

        // Common metric instruments
        private readonly Counter<double> _requestCounter;
        private readonly Histogram<double> _requestDuration;
        private readonly Counter<double> _errorCounter;
        private readonly UpDownCounter<int> _activeConnections;
        private readonly UpDownCounter<int> _activeRequests;
        private readonly Histogram<double> _databaseQueryDuration;

        // Custom metric instruments cache
        private readonly Dictionary<string, Counter<double>> _counters = new();
        private readonly Dictionary<string, Histogram<double>> _histograms = new();
        private readonly Dictionary<string, UpDownCounter<double>> _upDownCounters = new();
        private readonly Dictionary<string, ObservableGauge<double>> _gauges = new();
        private readonly Dictionary<string, Func<double>> _gaugeCallbacks = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="MetricsService"/> class.
        /// </summary>
        /// <param name="meter">The meter instance used to create metric instruments.</param>
        /// <param name="options">The observability configuration options.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="meter"/> or <paramref name="options"/> is null.</exception>
        public MetricsService(Meter meter, ObservabilityOptions options)
        {
            _meter = meter ?? throw new ArgumentNullException(nameof(meter));
            _options = options ?? throw new ArgumentNullException(nameof(options));

            // Initialize common metrics
            _requestCounter = _meter.CreateCounter<double>(
                "http_requests_total",
                description: "Total number of HTTP requests");

            _requestDuration = _meter.CreateHistogram<double>(
                "http_request_duration_ms",
                unit: "ms",
                description: "Duration of HTTP requests in milliseconds");

            _errorCounter = _meter.CreateCounter<double>(
                "errors_total",
                description: "Total number of errors");

            _activeConnections = _meter.CreateUpDownCounter<int>(
                "active_connections",
                description: "Number of active connections");

            _activeRequests = _meter.CreateUpDownCounter<int>(
                "http_server_active_requests",
                description: "Number of active HTTP requests");

            _databaseQueryDuration = _meter.CreateHistogram<double>(
                "database_query_duration_ms",
                unit: "ms",
                description: "Duration of database queries in milliseconds");
        }

        /// <inheritdoc/>
        public void IncrementCounter(string name, double value = 1, params KeyValuePair<string, object?>[] tags)
        {
            if (string.IsNullOrEmpty(name)) return;

            if (!_counters.TryGetValue(name, out var counter))
            {
                counter = _meter.CreateCounter<double>(name);
                _counters[name] = counter;
            }

            var enrichedTags = EnrichTags(tags);
            counter.Add(value, enrichedTags);
        }

        /// <inheritdoc/>
        public void RecordHistogram(string name, double value, params KeyValuePair<string, object?>[] tags)
        {
            if (string.IsNullOrEmpty(name)) return;

            if (!_histograms.TryGetValue(name, out var histogram))
            {
                histogram = _meter.CreateHistogram<double>(name);
                _histograms[name] = histogram;
            }

            var enrichedTags = EnrichTags(tags);
            histogram.Record(value, enrichedTags);
        }

        /// <inheritdoc/>
        public void SetGauge(string name, double value, params KeyValuePair<string, object?>[] tags)
        {
            if (string.IsNullOrEmpty(name)) return;

            // Store the value for the gauge callback
            var key = $"{name}_{string.Join("_", Array.ConvertAll(tags, t => $"{t.Key}={t.Value}"))}";
            _gaugeCallbacks[key] = () => value;

            if (!_gauges.ContainsKey(name))
            {
                var gauge = _meter.CreateObservableGauge<double>(name, () =>
                {
                    var measurements = new List<Measurement<double>>();
                    foreach (var kvp in _gaugeCallbacks)
                    {
                        if (kvp.Key.StartsWith(name))
                        {
                            measurements.Add(new Measurement<double>(kvp.Value(), EnrichTags(tags)));
                        }
                    }
                    return measurements;
                });
                _gauges[name] = gauge;
            }
        }

        /// <inheritdoc/>
        public void IncrementUpDownCounter(string name, double value = 1, params KeyValuePair<string, object?>[] tags)
        {
            if (string.IsNullOrEmpty(name)) return;

            if (!_upDownCounters.TryGetValue(name, out var counter))
            {
                counter = _meter.CreateUpDownCounter<double>(name);
                _upDownCounters[name] = counter;
            }

            var enrichedTags = EnrichTags(tags);
            counter.Add(value, enrichedTags);
        }

        /// <inheritdoc/>
        public void RecordRequestDuration(double durationMs, string method, string endpoint, int statusCode)
        {
            var tags = new KeyValuePair<string, object?>[]
            {
                    new("http.method", method),
                    new("http.route", endpoint),
                    new("http.status_code", statusCode),
                    new("http.status_class", GetStatusClass(statusCode))
            };

            _requestDuration.Record(durationMs, EnrichTags(tags));
        }

        /// <inheritdoc/>
        public void IncrementRequestCount(string method, string endpoint, int statusCode)
        {
            var tags = new KeyValuePair<string, object?>[]
            {
                    new("http.method", method),
                    new("http.route", endpoint),
                    new("http.status_code", statusCode),
                    new("http.status_class", GetStatusClass(statusCode))
            };

            _requestCounter.Add(1, EnrichTags(tags));
        }

        /// <inheritdoc/>
        public void RecordErrorCount(string errorType, string operation)
        {
            var tags = new KeyValuePair<string, object?>[]
            {
                    new("error.type", errorType),
                    new("operation", operation)
            };

            _errorCounter.Add(1, EnrichTags(tags));
        }

        /// <inheritdoc/>
        public void SetActiveConnections(int count)
        {
            _activeConnections.Add(count - GetCurrentActiveConnections());
        }

        /// <inheritdoc/>
        public void RecordDatabaseQueryDuration(double durationMs, string operation, string table)
        {
            var tags = new KeyValuePair<string, object?>[]
            {
                    new("db.operation", operation),
                    new("db.table", table)
            };

            _databaseQueryDuration.Record(durationMs, EnrichTags(tags));
        }

        /// <inheritdoc/>
        public void IncrementActiveRequests()
        {
            _activeRequests.Add(1);
        }

        /// <inheritdoc/>
        public void DecrementActiveRequests()
        {
            _activeRequests.Add(-1);
        }

        private KeyValuePair<string, object?>[] EnrichTags(KeyValuePair<string, object?>[] originalTags)
        {
            var enrichedTags = new List<KeyValuePair<string, object?>>(originalTags)
                {
                    new("service.name", _options.ServiceName),
                    new("service.version", _options.ServiceVersion)
                };

            if (!string.IsNullOrEmpty(_options.ServiceNamespace))
                enrichedTags.Add(new("service.namespace", _options.ServiceNamespace));

            if (!string.IsNullOrEmpty(_options.ServiceInstanceId))
                enrichedTags.Add(new("service.instance.id", _options.ServiceInstanceId));

            // Add custom service attributes
            foreach (var attribute in _options.ServiceAttributes)
            {
                enrichedTags.Add(new($"service.{attribute.Key}", attribute.Value));
            }

            return enrichedTags.ToArray();
        }

        private static string GetStatusClass(int statusCode)
        {
            return statusCode switch
            {
                >= 100 and < 200 => "1xx",
                >= 200 and < 300 => "2xx",
                >= 300 and < 400 => "3xx",
                >= 400 and < 500 => "4xx",
                >= 500 => "5xx",
                _ => "unknown"
            };
        }

        private int GetCurrentActiveConnections()
        {
            // This is a simplified implementation - in a real scenario,
            // you'd track this state more accurately
            return 0;
        }
    }

    /// <summary>
    /// Provides extension methods for <see cref="IMetricsService"/>.
    /// </summary>
    public static class MetricsServiceExtensions
    {
        /// <summary>
        /// Measures the execution time of a code block and records it as a histogram metric.
        /// </summary>
        /// <param name="metricsService">The metrics service instance.</param>
        /// <param name="metricName">The name of the metric to record.</param>
        /// <param name="tags">Optional tags to attach to the metric.</param>
        /// <returns>An <see cref="IDisposable"/> that records the duration when disposed.</returns>
        public static IDisposable MeasureTime(this IMetricsService metricsService, string metricName, params KeyValuePair<string, object?>[] tags)
        {
            return new TimeMeasurement(metricsService, metricName, tags);
        }

        /// <summary>
        /// Measures the execution time of an HTTP request and records it using <see cref="IMetricsService.RecordRequestDuration"/>.
        /// </summary>
        /// <param name="metricsService">The metrics service instance.</param>
        /// <param name="method">The HTTP method (e.g., GET, POST).</param>
        /// <param name="endpoint">The endpoint or route of the request.</param>
        /// <returns>An <see cref="IDisposable"/> that records the request duration when disposed.</returns>
        public static IDisposable MeasureRequestTime(this IMetricsService metricsService, string method, string endpoint)
        {
            return new RequestTimeMeasurement(metricsService, method, endpoint);
        }
    }

    internal class TimeMeasurement : IDisposable
    {
        private readonly IMetricsService _metricsService;
        private readonly string _metricName;
        private readonly KeyValuePair<string, object?>[] _tags;
        private readonly DateTime _startTime;

        public TimeMeasurement(IMetricsService metricsService, string metricName, KeyValuePair<string, object?>[] tags)
        {
            _metricsService = metricsService;
            _metricName = metricName;
            _tags = tags;
            _startTime = DateTime.UtcNow;
        }

        public void Dispose()
        {
            var duration = (DateTime.UtcNow - _startTime).TotalMilliseconds;
            _metricsService.RecordHistogram(_metricName, duration, _tags);
        }
    }

    internal class RequestTimeMeasurement : IDisposable
    {
        private readonly IMetricsService _metricsService;
        private readonly string _method;
        private readonly string _endpoint;
        private readonly DateTime _startTime;

        public RequestTimeMeasurement(IMetricsService metricsService, string method, string endpoint)
        {
            _metricsService = metricsService;
            _method = method;
            _endpoint = endpoint;
            _startTime = DateTime.UtcNow;
        }

        public void Dispose()
        {
            var duration = (DateTime.UtcNow - _startTime).TotalMilliseconds;
            // Note: Status code would need to be set externally or captured from context
            _metricsService.RecordRequestDuration(duration, _method, _endpoint, 200);
        }
    }
}
