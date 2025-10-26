#if NET48_OR_GREATER
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using mylogging.observability.Common;
using System.Web;

namespace mylogging.Extensions
{
    /// <summary>
    /// Extension methods for HttpApplication to enable request/response logging and observability features.
    /// </summary>
    public static class HttpApplicationExtensions
    {
        private static ILoggerFactory? _loggerFactory;

        /// <summary>
        /// Configures request and response logging for the HttpApplication.
        /// </summary>
        /// <param name="app">The HttpApplication instance.</param>
        /// <param name="serviceProvider">The service provider containing observability services.</param>
        /// <exception cref="ArgumentNullException">Thrown when app or serviceProvider is null.</exception>
        public static void UseRequestResponseLogging(
            this HttpApplication app,
            IServiceProvider serviceProvider)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));

            // Try to get logger factory from service provider first
            ILoggerFactory loggerFactory;
            try
            {
                loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                // Successfully obtained ILoggerFactory from service provider
            }
            catch (InvalidOperationException)
            {
                // Create a fallback logger factory if not available from DI
                if (_loggerFactory == null)
                {
                    _loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
                    {
                        builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
                    });
                }
                loggerFactory = _loggerFactory;
                // ILoggerFactory not available from DI, using fallback factory
            }

            // Set the logger factory for controllers to use
            LoggerFactoryProvider.SetLoggerFactory(loggerFactory);

            var options = serviceProvider.GetRequiredService<ObservabilityOptions>();
            // var redactionService = serviceProvider.GetRequiredService<IRedactionService>();

            // Optional services
            var tracingService = serviceProvider.GetService<ITracingService>();
            var metricsService = serviceProvider.GetService<IMetricsService>();

            //ServiceCollectionExtensions.ConfigureRequestResponseLogging(
            //    loggerFactory, 
            //    options, 
            //    redactionService, 
            //    tracingService, 
            //    metricsService);
        }
    }
}
#endif