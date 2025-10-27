#if NET48_OR_GREATER

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