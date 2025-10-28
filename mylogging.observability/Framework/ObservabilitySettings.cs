#if NET48_OR_GREATER

using System;
using System.Collections.Generic;
using System.Configuration;
using System.ComponentModel;
using System.Linq;

namespace mylogging.observability
{
    /// <summary>
    /// Configuration section for observability features including tracing, metrics, and logging.
    /// </summary>
    public class ObservabilitySettings : ConfigurationSection
    {
        /// <summary>
        /// Gets or sets the name of the service.
        /// </summary>
        [ConfigurationProperty("serviceName", DefaultValue = "MyApplication")]
        [Description("The name of the service.")]
        public string ServiceName
        {
            get => (string)this["serviceName"];
            set => this["serviceName"] = value;
        }

        /// <summary>
        /// Gets or sets the application name.
        /// </summary>
        [ConfigurationProperty("applicationName", DefaultValue = "MyApplication")]
        [Description("The application name.")]
        public string ApplicationName
        {
            get => (string)this["applicationName"];
            set => this["applicationName"] = value;
        }

        /// <summary>
        /// Gets or sets the business process name.
        /// </summary>
        [ConfigurationProperty("businessProcess", DefaultValue = "MyBusinessProcess")]
        [Description("The business process name.")]
        public string BusinessProcess
        {
            get => (string)this["businessProcess"];
            set => this["businessProcess"] = value;
        }

        /// <summary>
        /// Gets or sets the version of the service.
        /// </summary>
        [ConfigurationProperty("serviceVersion", DefaultValue = "1.0.0")]
        [Description("The version of the service.")]
        public string ServiceVersion
        {
            get => (string)this["serviceVersion"];
            set => this["serviceVersion"] = value;
        }

        /// <summary>
        /// Gets or sets the namespace of the service.
        /// </summary>
        [ConfigurationProperty("serviceNamespace", IsRequired = false)]
        [Description("The namespace of the service.")]
        public string ServiceNamespace
        {
            get => (string)this["serviceNamespace"];
            set => this["serviceNamespace"] = value;
        }

        /// <summary>
        /// Gets or sets the unique instance identifier of the service.
        /// </summary>
        [ConfigurationProperty("serviceInstanceId", IsRequired = false)]
        [Description("The unique instance identifier of the service.")]
        public string ServiceInstanceId
        {
            get => (string)this["serviceInstanceId"];
            set => this["serviceInstanceId"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether request and response logging is enabled.
        /// </summary>
        [ConfigurationProperty("enableRequestResponseLogging", DefaultValue = true)]
        [Description("Indicates whether request and response logging is enabled.")]
        public bool EnableRequestResponseLogging
        {
            get => (bool)this["enableRequestResponseLogging"];
            set => this["enableRequestResponseLogging"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether sensitive data redaction is enabled.
        /// </summary>
        [ConfigurationProperty("enableRedaction", DefaultValue = true)]
        [Description("Indicates whether sensitive data redaction is enabled.")]
        public bool EnableRedaction
        {
            get => (bool)this["enableRedaction"];
            set => this["enableRedaction"] = value;
        }

        /// <summary>
        /// Gets or sets the minimum log severity level.
        /// </summary>
        [ConfigurationProperty("logLevel", DefaultValue = ObservabilitySettingsLogSeverity.Information)]
        [Description("The minimum log severity level.")]
        public ObservabilitySettingsLogSeverity LogLevel
        {
            get => (ObservabilitySettingsLogSeverity)this["logLevel"];
            set => this["logLevel"] = value;
        }

        /// <summary>
        /// Gets or sets the batch size for exporting telemetry data.
        /// </summary>
        [ConfigurationProperty("exportBatchSize", DefaultValue = 100)]
        [Description("The batch size for exporting telemetry data.")]
        public int ExportBatchSize
        {
            get => (int)this["exportBatchSize"];
            set => this["exportBatchSize"] = value;
        }

        /// <summary>
        /// Gets or sets the timeout for exporting telemetry data (in seconds).
        /// </summary>
        [ConfigurationProperty("exportTimeoutSeconds", DefaultValue = 30)]
        [Description("The timeout for exporting telemetry data in seconds.")]
        public int ExportTimeoutSeconds
        {
            get => (int)this["exportTimeoutSeconds"];
            set => this["exportTimeoutSeconds"] = value;
        }

        /// <summary>
        /// Gets or sets the exporter configuration options.
        /// </summary>
        [ConfigurationProperty("exporter")]
        [Description("The exporter configuration options.")]
        public ExporterElement Exporter
        {
            get => (ExporterElement)this["exporter"];
            set => this["exporter"] = value;
        }

        /// <summary>
        /// Gets or sets the Splunk exporter configuration options.
        /// </summary>
        [ConfigurationProperty("splunkExporter")]
        [Description("The Splunk exporter configuration options.")]
        public SplunkExporterElement SplunkExporter
        {
            get => (SplunkExporterElement)this["splunkExporter"];
            set => this["splunkExporter"] = value;
        }

        /// <summary>
        /// Gets or sets the redaction configuration options.
        /// </summary>
        [ConfigurationProperty("redaction")]
        [Description("The redaction configuration options.")]
        public RedactionElement Redaction
        {
            get => (RedactionElement)this["redaction"];
            set => this["redaction"] = value;
        }

        /// <summary>
        /// Gets or sets the request and response logging configuration options.
        /// </summary>
        [ConfigurationProperty("requestResponseLogging")]
        [Description("The request and response logging configuration options.")]
        public RequestResponseLoggingElement RequestResponseLogging
        {
            get => (RequestResponseLoggingElement)this["requestResponseLogging"];
            set => this["requestResponseLogging"] = value;
        }

        /// <summary>
        /// Gets or sets the distributed tracing configuration options.
        /// </summary>
        [ConfigurationProperty("tracing")]
        [Description("The distributed tracing configuration options.")]
        public TracingElement Tracing
        {
            get => (TracingElement)this["tracing"];
            set => this["tracing"] = value;
        }

        /// <summary>
        /// Gets or sets the metrics collection configuration options.
        /// </summary>
        [ConfigurationProperty("metrics")]
        [Description("The metrics collection configuration options.")]
        public MetricsElement Metrics
        {
            get => (MetricsElement)this["metrics"];
            set => this["metrics"] = value;
        }

        /// <summary>
        /// Gets or sets the logging configuration options.
        /// </summary>
        [ConfigurationProperty("logging")]
        [Description("The logging configuration options.")]
        public LoggingElement Logging
        {
            get => (LoggingElement)this["logging"];
            set => this["logging"] = value;
        }

        /// <summary>
        /// Gets or sets additional custom attributes to attach to the service.
        /// </summary>
        [ConfigurationProperty("serviceAttributes")]
        [Description("Additional custom attributes to attach to the service.")]
        public ServiceAttributeCollection ServiceAttributes
        {
            get => (ServiceAttributeCollection)this["serviceAttributes"];
            set => this["serviceAttributes"] = value;
        }
        /// <summary>
        /// Maps the current ObservabilitySettings to an ObservabilityOptions instance.
        /// </summary>
        /// <returns>An ObservabilityOptions object populated from this settings instance.</returns>
        public ObservabilityOptions ToOptions()
        {
            var options = new ObservabilityOptions
            {
                ServiceName = this.ServiceName,
                ApplicationName = this.ApplicationName,
                BusinessProcess = this.BusinessProcess,
                ServiceVersion = this.ServiceVersion,
                ServiceNamespace = this.ServiceNamespace,
                ServiceInstanceId = this.ServiceInstanceId,
                EnableRequestResponseLogging = this.EnableRequestResponseLogging,
                EnableRedaction = this.EnableRedaction,
                LogLevel = (ObservabilityLogSeverity)this.LogLevel,
                ExportBatchSize = this.ExportBatchSize,
                ExportTimeout = TimeSpan.FromSeconds(this.ExportTimeoutSeconds),
                Exporter = this.Exporter != null ? new ExporterOptions
                {
                    EnableConsole = this.Exporter.EnableConsole,
                    EnableOtlp = this.Exporter.EnableOtlp,
                    OtlpEndpoint = this.Exporter.OtlpEndpoint,
                    Headers = this.Exporter.Headers?.Cast<KeyValueConfigurationElement>()
                        .ToDictionary(h => h.Key, h => h.Value) ?? new Dictionary<string, string>()
                } : null,
                SplunkExporter = this.SplunkExporter != null ? new SplunkExporter
                {
                    Url = this.SplunkExporter.Url,
                    Token = this.SplunkExporter.Token,
                    Source = this.SplunkExporter.Source,
                    Sourcetype = this.SplunkExporter.Sourcetype,
                    IsBatchJob = this.SplunkExporter.IsBatchJob,
                    Index = this.SplunkExporter.Index,
                    maxQueueSize = this.SplunkExporter.MaxQueueSize,
                    maxExportBatchSize = this.SplunkExporter.MaxExportBatchSize,
                    scheduledDelayMilliseconds = this.SplunkExporter.ScheduledDelayMilliseconds,
                    ExportTimeout = TimeSpan.FromSeconds(this.SplunkExporter.ExportTimeoutSeconds),
                    Host = this.SplunkExporter.Host
                } : null,
                Redaction = this.Redaction != null ? new RedactionOptions
                {
                    SensitiveKeys = this.Redaction.SensitiveKeys?.Cast<string>().ToList() ?? new List<string>(),
                    RedactionText = this.Redaction.RedactionText,
                    RedactHeaders = this.Redaction.RedactHeaders,
                    RedactQueryParams = this.Redaction.RedactQueryParams,
                    RedactRequestBody = this.Redaction.RedactRequestBody,
                    RedactResponseBody = this.Redaction.RedactResponseBody
                } : null!,
                RequestResponseLogging = this.RequestResponseLogging != null ? new RequestResponseLoggingOptions
                {
                    LogRequestHeaders = this.RequestResponseLogging.LogRequestHeaders,
                    LogResponseHeaders = this.RequestResponseLogging.LogResponseHeaders,
                    LogRequestBody = this.RequestResponseLogging.LogRequestBody,
                    LogResponseBody = this.RequestResponseLogging.LogResponseBody,
                    MaxBodySize = this.RequestResponseLogging.MaxBodySize,
                    ExcludePaths = this.RequestResponseLogging.ExcludePaths?.Cast<string>().ToList() ?? new List<string>(),
                    IncludeContentTypes = this.RequestResponseLogging.IncludeContentTypes?.Cast<string>().ToList() ?? new List<string>(),
                    // NEW: Add correlation header mappings
                    CorrelationIdHeaders = this.RequestResponseLogging.CorrelationIdHeaders?.Cast<string>().ToList() ?? new List<string>
                    {
                        "X-Correlation-Id", "CorrelationId", "x-correlation-id",
                        "correlationId", "Correlation-Id", "correlation-id"
                    },
                    ConsumerIdHeaders = this.RequestResponseLogging.ConsumerIdHeaders?.Cast<string>().ToList() ?? new List<string>
                    {
                        "X-Consumer-Id", "ConsumerId", "x-consumer-id",
                        "consumerId", "Consumer-Id", "consumer-id"
                    },
                    UserIdHeaders = this.RequestResponseLogging.UserIdHeaders?.Cast<string>().ToList() ?? new List<string>
                    {
                        "X-User-Id", "UserId", "x-user-id",
                        "userId", "User-Id", "user-id"
                    }
                } : null!,
                Tracing = this.Tracing != null ? new TracingOptions
                {
                    EnableCustomInstrumentation = this.Tracing.EnableCustomInstrumentation,
                    EnableHttpClientInstrumentation = this.Tracing.EnableHttpClientInstrumentation,
                    EnableHttpServerInstrumentation = this.Tracing.EnableHttpServerInstrumentation,
                    EnableSqlClientInstrumentation = this.Tracing.EnableSqlClientInstrumentation,
                    RecordException = this.Tracing.RecordException,
                    ActivitySources = this.Tracing.ActivitySources?.Cast<string>().ToList() ?? new List<string>(),
                    MaxTagValueLength = this.Tracing.MaxTagValueLength,
                    MaxEventCount = this.Tracing.MaxEventCount,
                    MaxLinkCount = this.Tracing.MaxLinkCount,
                    MaxTagCount = this.Tracing.MaxTagCount
                } : null!,
                Metrics = this.Metrics != null ? new MetricsOptions
                {
                    EnableCustomMetrics = this.Metrics.EnableCustomMetrics,
                    EnableHttpClientMetrics = this.Metrics.EnableHttpClientMetrics,
                    EnableHttpServerMetrics = this.Metrics.EnableHttpServerMetrics,
                    EnableRuntimeMetrics = this.Metrics.EnableRuntimeMetrics,
                    MeterNames = this.Metrics.MeterNames?.Cast<string>().ToList() ?? new List<string>(),
                    MaxMetricPointsPerMetric = this.Metrics.MaxMetricPointsPerMetric,
                    MetricExportInterval = TimeSpan.FromSeconds(this.Metrics.MetricExportIntervalSeconds),
                    MetricExportTimeout = TimeSpan.FromSeconds(this.Metrics.MetricExportTimeoutSeconds)
                } : null!,
                Logging = this.Logging != null ? new LoggingOptions
                {
                    EnableConsoleLogging = this.Logging.EnableConsoleLogging,
                    MinimumLevel = ParseLogLevel(this.Logging.MinimumLevel),
                    CategoryLevels = this.Logging.CategoryLevels?.Cast<KeyValueConfigurationElement>()
                        .ToDictionary(c => c.Key, c => ParseLogLevel(c.Value)) ?? new Dictionary<string, Microsoft.Extensions.Logging.LogLevel>()
                } : null!,
                ServiceAttributes = this.ServiceAttributes?.Cast<KeyValueConfigurationElement>()
                    .ToDictionary(a => a.Key, a => a.Value) ?? new Dictionary<string, string>()
            };
            return options;
        }

        /// <summary>
        /// Parses a string log level into Microsoft.Extensions.Logging.LogLevel.
        /// </summary>
        /// <param name="level">The string representation of the log level.</param>
        /// <returns>The parsed LogLevel.</returns>
        private static Microsoft.Extensions.Logging.LogLevel ParseLogLevel(string level)
        {
            if (string.IsNullOrEmpty(level))
                return Microsoft.Extensions.Logging.LogLevel.Information;

            if (Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(level, true, out var result))
                return result;

            return Microsoft.Extensions.Logging.LogLevel.Information;
        }
    }

    // --- Nested Elements ---

    /// <summary>
    /// Configuration element for exporter settings.
    /// </summary>
    public class ExporterElement : ConfigurationElement
    {
        /// <summary>
        /// Gets or sets a value indicating whether console exporter is enabled.
        /// </summary>
        [ConfigurationProperty("enableConsole", DefaultValue = true)]
        public bool EnableConsole
        {
            get => (bool)this["enableConsole"];
            set => this["enableConsole"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether OTLP exporter is enabled.
        /// </summary>
        [ConfigurationProperty("enableOtlp", DefaultValue = false)]
        public bool EnableOtlp
        {
            get => (bool)this["enableOtlp"];
            set => this["enableOtlp"] = value;
        }

        /// <summary>
        /// Gets or sets the OTLP endpoint URL.
        /// </summary>
        [ConfigurationProperty("otlpEndpoint", DefaultValue = "http://localhost:4317")]
        public string OtlpEndpoint
        {
            get => (string)this["otlpEndpoint"];
            set => this["otlpEndpoint"] = value;
        }

        /// <summary>
        /// Gets or sets custom headers to include in OTLP export requests.
        /// </summary>
        [ConfigurationProperty("headers")]
        public HeaderCollection Headers
        {
            get => (HeaderCollection)this["headers"];
            set => this["headers"] = value;
        }
    }

    /// <summary>
    /// Collection of header configuration elements.
    /// </summary>
    public class HeaderCollection : ConfigurationElementCollection, IEnumerable<KeyValueConfigurationElement>
    {
        /// <summary>
        /// Creates a new configuration element.
        /// </summary>
        /// <returns>A new KeyValueConfigurationElement instance.</returns>
        protected override ConfigurationElement CreateNewElement() => new KeyValueConfigurationElement();

        /// <summary>
        /// Gets the element key for a configuration element.
        /// </summary>
        /// <param name="element">The configuration element.</param>
        /// <returns>The element key.</returns>
        protected override object GetElementKey(ConfigurationElement element) => ((KeyValueConfigurationElement)element).Key;

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>An enumerator for the collection.</returns>
        public new IEnumerator<KeyValueConfigurationElement> GetEnumerator()
        {
            foreach (var key in BaseGetAllKeys())
                yield return (KeyValueConfigurationElement)BaseGet(key);
        }
    }

    /// <summary>
    /// Configuration element for key-value pairs.
    /// </summary>
    public class KeyValueConfigurationElement : ConfigurationElement
    {
        /// <summary>
        /// Gets or sets the key.
        /// </summary>
        [ConfigurationProperty("key", IsRequired = true, IsKey = true)]
        public string Key
        {
            get => (string)this["key"];
            set => this["key"] = value;
        }

        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        [ConfigurationProperty("value", IsRequired = true)]
        public string Value
        {
            get => (string)this["value"];
            set => this["value"] = value;
        }
    }

    /// <summary>
    /// Configuration element for redaction settings.
    /// </summary>
    public class RedactionElement : ConfigurationElement
    {
        /// <summary>
        /// Gets or sets the list of keys that contain sensitive data to be redacted.
        /// </summary>
        [ConfigurationProperty("sensitiveKeys")]
        public StringCollectionElement SensitiveKeys
        {
            get => (StringCollectionElement)this["sensitiveKeys"];
            set => this["sensitiveKeys"] = value;
        }

        /// <summary>
        /// Gets or sets the text to replace redacted values with.
        /// </summary>
        [ConfigurationProperty("redactionText", DefaultValue = "[REDACTED]")]
        public string RedactionText
        {
            get => (string)this["redactionText"];
            set => this["redactionText"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to redact sensitive headers.
        /// </summary>
        [ConfigurationProperty("redactHeaders", DefaultValue = true)]
        public bool RedactHeaders
        {
            get => (bool)this["redactHeaders"];
            set => this["redactHeaders"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to redact sensitive query parameters.
        /// </summary>
        [ConfigurationProperty("redactQueryParams", DefaultValue = true)]
        public bool RedactQueryParams
        {
            get => (bool)this["redactQueryParams"];
            set => this["redactQueryParams"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to redact sensitive request body content.
        /// </summary>
        [ConfigurationProperty("redactRequestBody", DefaultValue = true)]
        public bool RedactRequestBody
        {
            get => (bool)this["redactRequestBody"];
            set => this["redactRequestBody"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to redact sensitive response body content.
        /// </summary>
        [ConfigurationProperty("redactResponseBody", DefaultValue = true)]
        public bool RedactResponseBody
        {
            get => (bool)this["redactResponseBody"];
            set => this["redactResponseBody"] = value;
        }
    }

    /// <summary>
    /// Configuration element for request and response logging settings.
    /// </summary>
    public class RequestResponseLoggingElement : ConfigurationElement
    {
        /// <summary>
        /// Gets or sets a value indicating whether to log request headers.
        /// </summary>
        [ConfigurationProperty("logRequestHeaders", DefaultValue = true)]
        public bool LogRequestHeaders
        {
            get => (bool)this["logRequestHeaders"];
            set => this["logRequestHeaders"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to log response headers.
        /// </summary>
        [ConfigurationProperty("logResponseHeaders", DefaultValue = true)]
        public bool LogResponseHeaders
        {
            get => (bool)this["logResponseHeaders"];
            set => this["logResponseHeaders"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to log request body content.
        /// </summary>
        [ConfigurationProperty("logRequestBody", DefaultValue = true)]
        public bool LogRequestBody
        {
            get => (bool)this["logRequestBody"];
            set => this["logRequestBody"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to log response body content.
        /// </summary>
        [ConfigurationProperty("logResponseBody", DefaultValue = true)]
        public bool LogResponseBody
        {
            get => (bool)this["logResponseBody"];
            set => this["logResponseBody"] = value;
        }

        /// <summary>
        /// Gets or sets the maximum size in bytes for logged request/response bodies.
        /// </summary>
        [ConfigurationProperty("maxBodySize", DefaultValue = 4096)]
        public int MaxBodySize
        {
            get => (int)this["maxBodySize"];
            set => this["maxBodySize"] = value;
        }

        /// <summary>
        /// Gets or sets the list of URL paths to exclude from logging.
        /// </summary>
        [ConfigurationProperty("excludePaths")]
        public StringCollectionElement ExcludePaths
        {
            get => (StringCollectionElement)this["excludePaths"];
            set => this["excludePaths"] = value;
        }

        /// <summary>
        /// Gets or sets the list of content types to include in body logging.
        /// </summary>
        [ConfigurationProperty("includeContentTypes")]
        public StringCollectionElement IncludeContentTypes
        {
            get => (StringCollectionElement)this["includeContentTypes"];
            set => this["includeContentTypes"] = value;
        }

        /// <summary>
        /// Gets or sets the list of headers to use for correlation ID extraction.
        /// </summary>
        [ConfigurationProperty("correlationIdHeaders")]
        public StringCollectionElement CorrelationIdHeaders
        {
            get => (StringCollectionElement)this["correlationIdHeaders"];
            set => this["correlationIdHeaders"] = value;
        }

        /// <summary>
        /// Gets or sets the list of headers to use for consumer ID extraction.
        /// </summary>
        [ConfigurationProperty("consumerIdHeaders")]
        public StringCollectionElement ConsumerIdHeaders
        {
            get => (StringCollectionElement)this["consumerIdHeaders"];
            set => this["consumerIdHeaders"] = value;
        }

        /// <summary>
        /// Gets or sets the list of headers to use for user ID extraction.
        /// </summary>
        [ConfigurationProperty("userIdHeaders")]
        public StringCollectionElement UserIdHeaders
        {
            get => (StringCollectionElement)this["userIdHeaders"];
            set => this["userIdHeaders"] = value;
        }
    }

    /// <summary>
    /// Configuration element for distributed tracing settings.
    /// </summary>
    public class TracingElement : ConfigurationElement
    {
        /// <summary>
        /// Gets or sets a value indicating whether custom instrumentation is enabled.
        /// </summary>
        [ConfigurationProperty("enableCustomInstrumentation", DefaultValue = true)]
        public bool EnableCustomInstrumentation
        {
            get => (bool)this["enableCustomInstrumentation"];
            set => this["enableCustomInstrumentation"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether HTTP client instrumentation is enabled.
        /// </summary>
        [ConfigurationProperty("enableHttpClientInstrumentation", DefaultValue = true)]
        public bool EnableHttpClientInstrumentation
        {
            get => (bool)this["enableHttpClientInstrumentation"];
            set => this["enableHttpClientInstrumentation"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether HTTP server instrumentation is enabled.
        /// </summary>
        [ConfigurationProperty("enableHttpServerInstrumentation", DefaultValue = true)]
        public bool EnableHttpServerInstrumentation
        {
            get => (bool)this["enableHttpServerInstrumentation"];
            set => this["enableHttpServerInstrumentation"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether SQL client instrumentation is enabled.
        /// </summary>
        [ConfigurationProperty("enableSqlClientInstrumentation", DefaultValue = true)]
        public bool EnableSqlClientInstrumentation
        {
            get => (bool)this["enableSqlClientInstrumentation"];
            set => this["enableSqlClientInstrumentation"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to record exceptions in spans.
        /// </summary>
        [ConfigurationProperty("recordException", DefaultValue = true)]
        public bool RecordException
        {
            get => (bool)this["recordException"];
            set => this["recordException"] = value;
        }

        /// <summary>
        /// Gets or sets the list of custom activity source names to include.
        /// </summary>
        [ConfigurationProperty("activitySources")]
        public StringCollectionElement ActivitySources
        {
            get => (StringCollectionElement)this["activitySources"];
            set => this["activitySources"] = value;
        }

        /// <summary>
        /// Gets or sets the maximum length for tag values in traces.
        /// </summary>
        [ConfigurationProperty("maxTagValueLength", DefaultValue = 1024)]
        public int MaxTagValueLength
        {
            get => (int)this["maxTagValueLength"];
            set => this["maxTagValueLength"] = value;
        }

        /// <summary>
        /// Gets or sets the maximum number of events per span.
        /// </summary>
        [ConfigurationProperty("maxEventCount", DefaultValue = 128)]
        public int MaxEventCount
        {
            get => (int)this["maxEventCount"];
            set => this["maxEventCount"] = value;
        }

        /// <summary>
        /// Gets or sets the maximum number of links per span.
        /// </summary>
        [ConfigurationProperty("maxLinkCount", DefaultValue = 128)]
        public int MaxLinkCount
        {
            get => (int)this["maxLinkCount"];
            set => this["maxLinkCount"] = value;
        }

        /// <summary>
        /// Gets or sets the maximum number of tags per span.
        /// </summary>
        [ConfigurationProperty("maxTagCount", DefaultValue = 128)]
        public int MaxTagCount
        {
            get => (int)this["maxTagCount"];
            set => this["maxTagCount"] = value;
        }
    }

    /// <summary>
    /// Configuration element for metrics collection settings.
    /// </summary>
    public class MetricsElement : ConfigurationElement
    {
        /// <summary>
        /// Gets or sets a value indicating whether custom metrics are enabled.
        /// </summary>
        [ConfigurationProperty("enableCustomMetrics", DefaultValue = true)]
        public bool EnableCustomMetrics
        {
            get => (bool)this["enableCustomMetrics"];
            set => this["enableCustomMetrics"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether HTTP client metrics are enabled.
        /// </summary>
        [ConfigurationProperty("enableHttpClientMetrics", DefaultValue = true)]
        public bool EnableHttpClientMetrics
        {
            get => (bool)this["enableHttpClientMetrics"];
            set => this["enableHttpClientMetrics"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether HTTP server metrics are enabled.
        /// </summary>
        [ConfigurationProperty("enableHttpServerMetrics", DefaultValue = true)]
        public bool EnableHttpServerMetrics
        {
            get => (bool)this["enableHttpServerMetrics"];
            set => this["enableHttpServerMetrics"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether runtime metrics are enabled.
        /// </summary>
        [ConfigurationProperty("enableRuntimeMetrics", DefaultValue = true)]
        public bool EnableRuntimeMetrics
        {
            get => (bool)this["enableRuntimeMetrics"];
            set => this["enableRuntimeMetrics"] = value;
        }

        /// <summary>
        /// Gets or sets the list of custom meter names to include.
        /// </summary>
        [ConfigurationProperty("meterNames")]
        public StringCollectionElement MeterNames
        {
            get => (StringCollectionElement)this["meterNames"];
            set => this["meterNames"] = value;
        }

        /// <summary>
        /// Gets or sets the maximum number of metric points per metric.
        /// </summary>
        [ConfigurationProperty("maxMetricPointsPerMetric", DefaultValue = 2000)]
        public int MaxMetricPointsPerMetric
        {
            get => (int)this["maxMetricPointsPerMetric"];
            set => this["maxMetricPointsPerMetric"] = value;
        }

        /// <summary>
        /// Gets or sets the interval for exporting metrics (in seconds).
        /// </summary>
        [ConfigurationProperty("metricExportIntervalSeconds", DefaultValue = 60)]
        public int MetricExportIntervalSeconds
        {
            get => (int)this["metricExportIntervalSeconds"];
            set => this["metricExportIntervalSeconds"] = value;
        }

        /// <summary>
        /// Gets or sets the timeout for exporting metrics (in seconds).
        /// </summary>
        [ConfigurationProperty("metricExportTimeoutSeconds", DefaultValue = 30)]
        public int MetricExportTimeoutSeconds
        {
            get => (int)this["metricExportTimeoutSeconds"];
            set => this["metricExportTimeoutSeconds"] = value;
        }
    }

    /// <summary>
    /// Configuration element for logging settings.
    /// </summary>
    public class LoggingElement : ConfigurationElement
    {
        /// <summary>
        /// Gets or sets a value indicating whether console logging is enabled.
        /// </summary>
        [ConfigurationProperty("enableConsoleLogging", DefaultValue = true)]
        public bool EnableConsoleLogging
        {
            get => (bool)this["enableConsoleLogging"];
            set => this["enableConsoleLogging"] = value;
        }

        /// <summary>
        /// Gets or sets the minimum log level for all categories.
        /// </summary>
        [ConfigurationProperty("minimumLevel", DefaultValue = "Information")]
        public string MinimumLevel
        {
            get => (string)this["minimumLevel"];
            set => this["minimumLevel"] = value;
        }

        /// <summary>
        /// Gets or sets category-specific log levels.
        /// </summary>
        [ConfigurationProperty("categoryLevels")]
        public CategoryLevelCollection CategoryLevels
        {
            get => (CategoryLevelCollection)this["categoryLevels"];
            set => this["categoryLevels"] = value;
        }
    }

    /// <summary>
    /// Collection of category-level configuration elements.
    /// </summary>
    public class CategoryLevelCollection : ConfigurationElementCollection, IEnumerable<KeyValueConfigurationElement>
    {
        /// <summary>
        /// Creates a new configuration element.
        /// </summary>
        /// <returns>A new KeyValueConfigurationElement instance.</returns>
        protected override ConfigurationElement CreateNewElement() => new KeyValueConfigurationElement();

        /// <summary>
        /// Gets the element key for a configuration element.
        /// </summary>
        /// <param name="element">The configuration element.</param>
        /// <returns>The element key.</returns>
        protected override object GetElementKey(ConfigurationElement element) => ((KeyValueConfigurationElement)element).Key;

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>An enumerator for the collection.</returns>
        public new IEnumerator<KeyValueConfigurationElement> GetEnumerator()
        {
            foreach (var key in BaseGetAllKeys())
                yield return (KeyValueConfigurationElement)BaseGet(key);
        }
    }

    /// <summary>
    /// Configuration element for Splunk exporter settings.
    /// </summary>
    public class SplunkExporterElement : ConfigurationElement
    {
        /// <summary>
        /// Gets or sets the Splunk HTTP Event Collector (HEC) endpoint URL.
        /// </summary>
        [ConfigurationProperty("url", DefaultValue = "http://localhost:8088")]
        public string Url
        {
            get => (string)this["url"];
            set => this["url"] = value;
        }

        /// <summary>
        /// Gets or sets the Splunk authentication token.
        /// </summary>
        [ConfigurationProperty("token", DefaultValue = "your-splunk-token")]
        public string Token
        {
            get => (string)this["token"];
            set => this["token"] = value;
        }

        /// <summary>
        /// Gets or sets the source value for Splunk events.
        /// </summary>
        [ConfigurationProperty("source", DefaultValue = "my-application")]
        public string Source
        {
            get => (string)this["source"];
            set => this["source"] = value;
        }

        /// <summary>
        /// Gets or sets the sourcetype value for Splunk events.
        /// </summary>
        [ConfigurationProperty("sourcetype", DefaultValue = "_json")]
        public string Sourcetype
        {
            get => (string)this["sourcetype"];
            set => this["sourcetype"] = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the exporter is used in batch job mode.
        /// </summary>
        [ConfigurationProperty("isBatchJob", DefaultValue = true)]
        public bool IsBatchJob
        {
            get => (bool)this["isBatchJob"];
            set => this["isBatchJob"] = value;
        }

        /// <summary>
        /// Gets or sets the Splunk index to send events to.
        /// </summary>
        [ConfigurationProperty("index", DefaultValue = "main")]
        public string Index
        {
            get => (string)this["index"];
            set => this["index"] = value;
        }

        /// <summary>
        /// Gets or sets the maximum queue size for the exporter.
        /// </summary>
        [ConfigurationProperty("maxQueueSize", DefaultValue = 2048)]
        public int MaxQueueSize
        {
            get => (int)this["maxQueueSize"];
            set => this["maxQueueSize"] = value;
        }

        /// <summary>
        /// Gets or sets the maximum export batch size for the exporter.
        /// </summary>
        [ConfigurationProperty("maxExportBatchSize", DefaultValue = 512)]
        public int MaxExportBatchSize
        {
            get => (int)this["maxExportBatchSize"];
            set => this["maxExportBatchSize"] = value;
        }

        /// <summary>
        /// Gets or sets the scheduled delay in milliseconds for batch exporting.
        /// </summary>
        [ConfigurationProperty("scheduledDelayMilliseconds", DefaultValue = 5000)]
        public int ScheduledDelayMilliseconds
        {
            get => (int)this["scheduledDelayMilliseconds"];
            set => this["scheduledDelayMilliseconds"] = value;
        }

        /// <summary>
        /// Gets or sets the timeout for exporting telemetry data to Splunk (in seconds).
        /// </summary>
        [ConfigurationProperty("exportTimeoutSeconds", DefaultValue = 30)]
        public int ExportTimeoutSeconds
        {
            get => (int)this["exportTimeoutSeconds"];
            set => this["exportTimeoutSeconds"] = value;
        }

        /// <summary>
        /// Gets or sets the host value for Splunk events.
        /// </summary>
        [ConfigurationProperty("host", IsRequired = false)]
        public string Host
        {
            get => (string)this["host"];
            set => this["host"] = value;
        }
    }

    /// <summary>
    /// Collection of string configuration elements.
    /// </summary>
    public class StringCollectionElement : ConfigurationElementCollection, IEnumerable<string>
    {
        /// <summary>
        /// Creates a new configuration element.
        /// </summary>
        /// <returns>A new StringElement instance.</returns>
        protected override ConfigurationElement CreateNewElement() => new StringElement();

        /// <summary>
        /// Gets the element key for a configuration element.
        /// </summary>
        /// <param name="element">The configuration element.</param>
        /// <returns>The element key.</returns>
        protected override object GetElementKey(ConfigurationElement element) => ((StringElement)element).Value;

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>An enumerator for the collection.</returns>
        public new IEnumerator<string> GetEnumerator()
        {
            foreach (var key in BaseGetAllKeys())
                yield return ((StringElement)BaseGet(key)).Value;
        }
    }

    /// <summary>
    /// Configuration element for string values.
    /// </summary>
    public class StringElement : ConfigurationElement
    {
        /// <summary>
        /// Gets or sets the string value.
        /// </summary>
        [ConfigurationProperty("value", IsRequired = true, IsKey = true)]
        public string Value
        {
            get => (string)this["value"];
            set => this["value"] = value;
        }
    }

    /// <summary>
    /// Collection of service attribute configuration elements.
    /// </summary>
    public class ServiceAttributeCollection : ConfigurationElementCollection, IEnumerable<KeyValueConfigurationElement>
    {
        /// <summary>
        /// Creates a new configuration element.
        /// </summary>
        /// <returns>A new KeyValueConfigurationElement instance.</returns>
        protected override ConfigurationElement CreateNewElement() => new KeyValueConfigurationElement();

        /// <summary>
        /// Gets the element key for a configuration element.
        /// </summary>
        /// <param name="element">The configuration element.</param>
        /// <returns>The element key.</returns>
        protected override object GetElementKey(ConfigurationElement element) => ((KeyValueConfigurationElement)element).Key;

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>An enumerator for the collection.</returns>
        public new IEnumerator<KeyValueConfigurationElement> GetEnumerator()
        {
            foreach (var key in BaseGetAllKeys())
                yield return (KeyValueConfigurationElement)BaseGet(key);
        }
    }
    /// <summary>
    /// Defines log severity levels for filtering and categorization.
    /// </summary>
    public enum ObservabilitySettingsLogSeverity
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
#endif