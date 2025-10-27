#if NET48_OR_GREATER

using Microsoft.Extensions.Logging;
using mylogging.observability.Common;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace mylogging.observability.Framework
{
    /// <summary>
    /// Configuration helper for setting up the TracerProvider
    /// </summary>
    public static class TelemetryConfiguration
    {
        private static TracerProvider? _tracerProvider;
        private static ObservabilityOptions? _options;
        private static ILoggerFactory? _loggerFactory;

        /// <summary>
        /// Gets the current ObservabilityOptions instance.
        /// </summary>
        public static ObservabilityOptions? Options => _options;

        /// <summary>
        /// Gets the current ILoggerFactory instance.
        /// </summary>
        public static ILoggerFactory? LoggerFactory => _loggerFactory;

        /// <summary>
        /// Initializes the TracerProvider with ObservabilityOptions
        /// </summary>
        /// <param name="options">The observability options to use</param>
        /// <param name="configure">Optional action to configure the TracerProviderBuilder</param>
        public static void Initialize(ObservabilityOptions options, Action<TracerProviderBuilder>? configure = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            var resourceBuilder = OpenTelemetry.Resources.ResourceBuilder.CreateDefault()
                .AddService(
                    serviceName: options.ServiceName,
                    serviceVersion: options.ServiceVersion,
                    serviceNamespace: options.ServiceNamespace,
                    serviceInstanceId: options.ServiceInstanceId);

            // Add custom service attributes
            if (options.ServiceAttributes != null && options.ServiceAttributes.Count > 0)
            {
                foreach (var attr in options.ServiceAttributes)
                {
                    resourceBuilder.AddAttributes(new[] { new KeyValuePair<string, object>(attr.Key, attr.Value) });
                }
            }

            var builder = Sdk.CreateTracerProviderBuilder()
                .AddSource(options.ServiceName)  // Match the HttpModule's ActivitySource name
                .AddSource("Telemetry")       // Keep for backward compatibility
                .SetResourceBuilder(resourceBuilder)
                .SetSampler(new AlwaysOnSampler());

            // Configure tracing options if available
            if (options.Tracing != null)
            {
                // Add activity sources from configuration
                if (options.Tracing.ActivitySources != null)
                {
                    foreach (var source in options.Tracing.ActivitySources)
                    {
                        builder.AddSource(source);
                    }
                }
            }

            // Allow custom configuration
            configure?.Invoke(builder);

            _tracerProvider = builder.Build();
        }

        /// <summary>
        /// Initializes the TracerProvider with optional custom configuration
        /// </summary>
        /// <param name="configure">Optional action to configure the TracerProviderBuilder</param>
        public static void Initialize(Action<TracerProviderBuilder>? configure = null)
        {
            var builder = Sdk.CreateTracerProviderBuilder()
                .AddSource("Telemetry")
                .SetResourceBuilder(
                    OpenTelemetry.Resources.ResourceBuilder.CreateDefault()
                        .AddService(
                            serviceName: GetServiceName(),
                            serviceVersion: GetServiceVersion()))
                .SetSampler(new AlwaysOnSampler());

            // Allow custom configuration
            configure?.Invoke(builder);

            _tracerProvider = builder.Build();
        }

        /// <summary>
        /// Shuts down and disposes the TracerProvider
        /// </summary>
        public static void Shutdown()
        {
            _tracerProvider?.Dispose();
            _loggerFactory = null;
            _options = null;
        }

        private static string GetServiceName()
        {
            return System.Configuration.ConfigurationManager.AppSettings["ServiceName"]
                   ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
        }

        private static string GetServiceVersion()
        {
            return System.Configuration.ConfigurationManager.AppSettings["ServiceVersion"]
                   ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                   ?? "1.0.0";
        }
    }

    /// <summary>
    /// Fluent builder for configuring observability in .NET Framework applications
    /// </summary>
    public class ObservabilityBuilder
    {
        private readonly ObservabilityOptions _options;
        private Action<ILoggingBuilder>? _loggingConfiguration;
        private Action<TracerProviderBuilder>? _tracingConfiguration;
        private bool _enableConsoleLogging;
        private bool _enableDebugLogging;
        private LogLevel _minimumLogLevel = LogLevel.Information;

        internal ObservabilityBuilder(ObservabilityOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Configures logging for the observability system
        /// </summary>
        /// <param name="configure">Action to configure logging</param>
        /// <returns>The builder for method chaining</returns>
        public ObservabilityBuilder WithLogging(Action<ILoggingBuilder>? configure = null)
        {
            _loggingConfiguration = configure;
            return this;
        }

        /// <summary>
        /// Enables console logging with optional minimum log level
        /// </summary>
        /// <param name="minimumLevel">The minimum log level</param>
        /// <returns>The builder for method chaining</returns>
        public ObservabilityBuilder WithConsoleLogging(LogLevel minimumLevel = LogLevel.Information)
        {
            _enableConsoleLogging = true;
            _minimumLogLevel = minimumLevel;
            return this;
        }

        /// <summary>
        /// Enables debug logging (writes to Debug output)
        /// </summary>
        /// <returns>The builder for method chaining</returns>
        public ObservabilityBuilder WithDebugLogging()
        {
            _enableDebugLogging = true;
            return this;
        }

        /// <summary>
        /// Configures distributed tracing for the observability system
        /// </summary>
        /// <param name="configure">Action to configure tracing</param>
        /// <returns>The builder for method chaining</returns>
        public ObservabilityBuilder WithTracing(Action<TracerProviderBuilder>? configure = null)
        {
            _tracingConfiguration = configure;
            return this;
        }

        /// <summary>
        /// Configures metrics collection for the observability system
        /// </summary>
        /// <param name="configure">Action to configure metrics</param>
        /// <returns>The builder for method chaining</returns>
        public ObservabilityBuilder WithMetrics(Action<MetricsConfiguration>? configure = null)
        {
            // Metrics configuration
            configure?.Invoke(new MetricsConfiguration(_options));
            return this;
        }

        /// <summary>
        /// Builds and initializes the observability configuration
        /// </summary>
        public void Build()
        {
            // Configure logging
            var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(_minimumLogLevel);

                // Apply custom logging configuration
                _loggingConfiguration?.Invoke(builder);
            });

            // Set the logger factory in the static provider
            LoggerFactoryProvider.SetLoggerFactory(loggerFactory);

            // Configure tracing
            TelemetryConfiguration.Initialize(_options, builder =>
            {
                // Apply custom tracing configuration
                _tracingConfiguration?.Invoke(builder);
            });
        }
    }

    /// <summary>
    /// Configuration class for metrics
    /// </summary>
    public class MetricsConfiguration
    {
        private readonly ObservabilityOptions _options;

        internal MetricsConfiguration(ObservabilityOptions options)
        {
            _options = options;
        }

        /// <summary>
        /// Enables custom metrics collection
        /// </summary>
        /// <returns>The metrics configuration for method chaining</returns>
        public MetricsConfiguration EnableCustomMetrics()
        {
            if (_options.Metrics != null)
            {
                _options.Metrics.EnableCustomMetrics = true;
            }
            return this;
        }

        /// <summary>
        /// Enables HTTP client metrics
        /// </summary>
        /// <returns>The metrics configuration for method chaining</returns>
        public MetricsConfiguration EnableHttpClientMetrics()
        {
            if (_options.Metrics != null)
            {
                _options.Metrics.EnableHttpClientMetrics = true;
            }
            return this;
        }

        /// <summary>
        /// Enables HTTP server metrics
        /// </summary>
        /// <returns>The metrics configuration for method chaining</returns>
        public MetricsConfiguration EnableHttpServerMetrics()
        {
            if (_options.Metrics != null)
            {
                _options.Metrics.EnableHttpServerMetrics = true;
            }
            return this;
        }

        /// <summary>
        /// Enables runtime metrics
        /// </summary>
        /// <returnsThe metrics configuration for method chaining</returns>
        public MetricsConfiguration EnableRuntimeMetrics()
        {
            if (_options.Metrics != null)
            {
                _options.Metrics.EnableRuntimeMetrics = true;
            }
            return this;
        }
    }

    /// <summary>
    /// Extension methods for adding observability to .NET Framework applications
    /// </summary>
    public static class ObservabilityExtensions
    {
        /// <summary>
        /// Adds observability services with the specified options using a fluent builder pattern
        /// </summary>
        /// <param name="options">The observability options</param>
        /// <param name="configure">Action to configure the observability builder</param>
        /// <returns>The configured ObservabilityOptions</returns>
        /// <example>
        /// <code>
        /// // In Global.asax.cs Application_Start:
        /// var options = new ObservabilityOptions 
        /// { 
        ///     ServiceName = "MyWcfService",
        ///     ServiceVersion = "1.0.0"
        /// };
        /// 
        /// options.AddObservability(builder => builder
        ///     .WithConsoleLogging(LogLevel.Information)
        ///     .WithDebugLogging()
        ///     .WithTracing(tracing => tracing
        ///         .AddConsoleExporter()
        ///         .AddOtlpExporter(otlp => otlp.Endpoint = new Uri("http://localhost:4317")))
        ///     .WithMetrics(metrics => metrics
        ///         .EnableHttpServerMetrics()
        ///         .EnableCustomMetrics())
        ///     .Build());
        /// </code>
        /// </example>
        public static ObservabilityOptions AddObservability(
            this ObservabilityOptions options,
            Action<ObservabilityBuilder> configure)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            var builder = new ObservabilityBuilder(options);
            configure(builder);
            
            return options;
        }

        /// <summary>
        /// Adds observability services with default configuration
        /// </summary>
        /// <param name="options">The observability options</param>
        /// <returns>The configured ObservabilityOptions</returns>
        /// <example>
        /// <code>
        /// // In Global.asax.cs Application_Start:
        /// var options = new ObservabilityOptions 
        /// { 
        ///     ServiceName = "MyWcfService",
        ///     ServiceVersion = "1.0.0"
        /// }.AddObservability();
        /// </code>
        /// </example>
        public static ObservabilityOptions AddObservability(this ObservabilityOptions options)
        {
            return options.AddObservability(builder => builder
                .WithConsoleLogging(LogLevel.Information)
                .WithDebugLogging()
                .WithTracing()
                .Build());
        }
    }
}
#endif