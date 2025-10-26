using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace mylogging.observability.Common
{
    /// <summary>
    /// Provides centralized instrumentation sources for observability including activity tracing and metrics.
    /// </summary>
    public static class InstrumentationSources
    {
        /// <summary>
        /// The name used for the ActivitySource instance.
        /// </summary>
        public const string ActivitySourceName = "MyCompany.Observability";

        /// <summary>
        /// The name used for the Meter instance.
        /// </summary>
        public const string MeterName = "MyCompany.Observability";

        /// <summary>
        /// The version of the instrumentation sources.
        /// </summary>
        public const string Version = "1.0.0";

        private static readonly ActivitySource _activitySource = new(ActivitySourceName, Version);
        private static readonly Meter _meter = new(MeterName, Version);

        /// <summary>
        /// Gets the shared ActivitySource instance for distributed tracing.
        /// </summary>
        public static ActivitySource ActivitySource => _activitySource;

        /// <summary>
        /// Gets the shared Meter instance for metrics collection.
        /// </summary>
        public static Meter Meter => _meter;

        /// <summary>
        /// Disposes the ActivitySource and Meter instances.
        /// </summary>
        public static void Dispose()
        {
            _activitySource?.Dispose();
            _meter?.Dispose();
        }
    }
}
