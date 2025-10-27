#if NET48_OR_GREATER

using mylogging.observability.Common;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;
using System.Collections.Generic;
using System.Text;

namespace mylogging.observability.Framework
{
    /// <summary>
    /// Configuration helper for setting up the TracerProvider
    /// </summary>
    public static class WcfTelemetryConfiguration
    {
        private static TracerProvider? _tracerProvider;
        private static ObservabilityOptions? _options;

        /// <summary>
        /// Gets the current ObservabilityOptions instance.
        /// </summary>
        public static ObservabilityOptions? Options => _options;

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
                .AddSource($"{options.ServiceName}.WCF")  // Match the HttpModule's ActivitySource name
                .AddSource("WCF.Custom.Telemetry")       // Keep for backward compatibility
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
                .AddSource("WCF.Custom.Telemetry")
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
}
#endif