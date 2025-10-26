namespace mylogging.observability.Common
{
    /// <summary>
    /// Configuration options for observability features including tracing, metrics, and logging.
    /// </summary>
    public class ObservabilityOptions
    {
        /// <summary>
        /// Gets or sets the name of the service.
        /// </summary>
        public string ServiceName { get; set; } = "MyApplication";

        /// <summary>
        /// Gets or sets the version of the service.
        /// </summary>
        public string ServiceVersion { get; set; } = "1.0.0";

        /// <summary>
        /// Gets or sets the namespace of the service.
        /// </summary>
        public string? ServiceNamespace { get; set; }

        /// <summary>
        /// Gets or sets the unique instance identifier of the service.
        /// </summary>
        public string? ServiceInstanceId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether request and response logging is enabled.
        /// </summary>
        public bool EnableRequestResponseLogging { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether sensitive data redaction is enabled.
        /// </summary>
        public bool EnableRedaction { get; set; } = true;

        /// <summary>
        /// Gets or sets the minimum log severity level.
        /// </summary>
        public LogSeverity LogLevel { get; set; } = LogSeverity.Information;

        /// <summary>
        /// Gets or sets the batch size for exporting telemetry data.
        /// </summary>
        public int ExportBatchSize { get; set; } = 100;

        /// <summary>
        /// Gets or sets the timeout for exporting telemetry data.
        /// </summary>
        public TimeSpan ExportTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the exporter configuration options.
        /// </summary>
        public ExporterOptions? Exporter { get; set; } = new ExporterOptions();

        /// <summary>
        /// Gets or sets the redaction configuration options.
        /// </summary>
        public RedactionOptions Redaction { get; set; } = new RedactionOptions();

        /// <summary>
        /// Gets or sets the request and response logging configuration options.
        /// </summary>
        public RequestResponseLoggingOptions RequestResponseLogging { get; set; } = new RequestResponseLoggingOptions();

        /// <summary>
        /// Gets or sets the distributed tracing configuration options.
        /// </summary>
        public TracingOptions Tracing { get; set; } = new TracingOptions();

        /// <summary>
        /// Gets or sets the metrics collection configuration options.
        /// </summary>
        public MetricsOptions Metrics { get; set; } = new MetricsOptions();

        /// <summary>
        /// Gets or sets the logging configuration options.
        /// </summary>
        public LoggingOptions Logging { get; set; } = new LoggingOptions();

        /// <summary>
        /// Gets or sets additional custom attributes to attach to the service.
        /// </summary>
        public Dictionary<string, string> ServiceAttributes { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Configuration options for telemetry data exporters.
    /// </summary>
    public class ExporterOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether console exporter is enabled.
        /// </summary>
        public bool EnableConsole { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether OTLP (OpenTelemetry Protocol) exporter is enabled.
        /// </summary>
        public bool EnableOtlp { get; set; } = false;

        /// <summary>
        /// Gets or sets the OTLP endpoint URL.
        /// </summary>
        public string OtlpEndpoint { get; set; } = "http://localhost:4317";

        /// <summary>
        /// Gets or sets custom headers to include in OTLP export requests.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Configuration options for sensitive data redaction.
    /// </summary>
    public class RedactionOptions
    {
        /// <summary>
        /// Gets or sets the list of keys that contain sensitive data to be redacted.
        /// </summary>
        public List<string> SensitiveKeys { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the text to replace redacted values with.
        /// </summary>
        public string RedactionText { get; set; } = "[REDACTED]";

        /// <summary>
        /// Gets or sets a value indicating whether to redact sensitive headers.
        /// </summary>
        public bool RedactHeaders { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to redact sensitive query parameters.
        /// </summary>
        public bool RedactQueryParams { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to redact sensitive request body content.
        /// </summary>
        public bool RedactRequestBody { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to redact sensitive response body content.
        /// </summary>
        public bool RedactResponseBody { get; set; } = true;
    }

    /// <summary>
    /// Configuration options for HTTP request and response logging.
    /// </summary>
    public class RequestResponseLoggingOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether to log request headers.
        /// </summary>
        public bool LogRequestHeaders { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to log response headers.
        /// </summary>
        public bool LogResponseHeaders { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to log request body content.
        /// </summary>
        public bool LogRequestBody { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to log response body content.
        /// </summary>
        public bool LogResponseBody { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum size in bytes for logged request/response bodies.
        /// </summary>
        public int MaxBodySize { get; set; } = 4096;

        /// <summary>
        /// Gets or sets the list of URL paths to exclude from logging.
        /// </summary>
        public List<string> ExcludePaths { get; set; } = new List<string> { "/health", "/metrics" };

        /// <summary>
        /// Gets or sets the list of content types to include in body logging.
        /// </summary>
        public List<string> IncludeContentTypes { get; set; } = new List<string>
            {
                "application/json",
                "application/xml",
                "text/plain",
                "text/xml"
            };
    }

    /// <summary>
    /// Configuration options for distributed tracing.
    /// </summary>
    public class TracingOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether custom instrumentation is enabled.
        /// </summary>
        public bool EnableCustomInstrumentation { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether HTTP client instrumentation is enabled.
        /// </summary>
        public bool EnableHttpClientInstrumentation { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether HTTP server instrumentation is enabled.
        /// </summary>
        public bool EnableHttpServerInstrumentation { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether SQL client instrumentation is enabled.
        /// </summary>
        public bool EnableSqlClientInstrumentation { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to record exceptions in spans.
        /// </summary>
        public bool RecordException { get; set; } = true;

        /// <summary>
        /// Gets or sets the list of custom activity source names to include.
        /// </summary>
        public List<string> ActivitySources { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the maximum length for tag values in traces.
        /// </summary>
        public int MaxTagValueLength { get; set; } = 1024;

        /// <summary>
        /// Gets or sets the maximum number of events per span.
        /// </summary>
        public int MaxEventCount { get; set; } = 128;

        /// <summary>
        /// Gets or sets the maximum number of links per span.
        /// </summary>
        public int MaxLinkCount { get; set; } = 128;

        /// <summary>
        /// Gets or sets the maximum number of tags per span.
        /// </summary>
        public int MaxTagCount { get; set; } = 128;
    }

    /// <summary>
    /// Configuration options for metrics collection.
    /// </summary>
    public class MetricsOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether custom metrics are enabled.
        /// </summary>
        public bool EnableCustomMetrics { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether HTTP client metrics are enabled.
        /// </summary>
        public bool EnableHttpClientMetrics { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether HTTP server metrics are enabled.
        /// </summary>
        public bool EnableHttpServerMetrics { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether runtime metrics are enabled.
        /// </summary>
        public bool EnableRuntimeMetrics { get; set; } = true;

        /// <summary>
        /// Gets or sets the list of custom meter names to include.
        /// </summary>
        public List<string> MeterNames { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the maximum number of metric points per metric.
        /// </summary>
        public int MaxMetricPointsPerMetric { get; set; } = 2000;

        /// <summary>
        /// Gets or sets the interval for exporting metrics.
        /// </summary>
        public TimeSpan MetricExportInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Gets or sets the timeout for exporting metrics.
        /// </summary>
        public TimeSpan MetricExportTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Configuration options for application logging.
    /// </summary>
    public class LoggingOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether console logging is enabled.
        /// </summary>
        public bool EnableConsoleLogging { get; set; } = true;

        /// <summary>
        /// Gets or sets the minimum log level for all categories.
        /// </summary>
        public Microsoft.Extensions.Logging.LogLevel MinimumLevel { get; set; } = Microsoft.Extensions.Logging.LogLevel.Information;

        /// <summary>
        /// Gets or sets category-specific log levels.
        /// </summary>
        public Dictionary<string, Microsoft.Extensions.Logging.LogLevel> CategoryLevels { get; set; } = new Dictionary<string, Microsoft.Extensions.Logging.LogLevel>();
    }

    /// <summary>
    /// Defines log severity levels for filtering and categorization.
    /// </summary>
    public enum LogSeverity
    {
        /// <summary>
        /// Trace level logging - most detailed.
        /// </summary>
        Trace = 0,

        /// <summary>
        /// Debug level logging - detailed information for diagnostics.
        /// </summary>
        Debug = 1,

        /// <summary>
        /// Information level logging - general informational messages.
        /// </summary>
        Information = 2,

        /// <summary>
        /// Warning level logging - potentially harmful situations.
        /// </summary>
        Warning = 3,

        /// <summary>
        /// Error level logging - error events that might still allow the application to continue.
        /// </summary>
        Error = 4,

        /// <summary>
        /// Critical level logging - critical failures that require immediate attention.
        /// </summary>
        Critical = 5,

        /// <summary>
        /// No logging - disables all logging.
        /// </summary>
        None = 6
    }
}