using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using mylogging.observability;
using OpenTelemetry.Trace;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace RedactionWcf
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
            
            // Load observability configuration from Web.config
            var settings = (ObservabilitySettings)ConfigurationManager.GetSection("observability");
            var configOptions = settings?.ToOptions() ?? new ObservabilityOptions
            {
                ServiceName = "RedactionWcfService",
                ServiceVersion = "1.0.0",
                ApplicationName = "RedactionWcf",
                BusinessProcess = "Healthcare"
            };
            
            // ✅ NEW: Use fluent AddObservability extension method
            configOptions.AddObservability(builder => builder
                .WithLogging(logging =>
                {
                    // Configure logging here if needed
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .WithTracing(tracing =>
                {
                    // Configure OpenTelemetry tracing
                    tracing.AddConsoleExporter(options =>
                    {
                        options.Targets = OpenTelemetry.Exporter.ConsoleExporterOutputTargets.Console;
                    })
                    .AddSource(configOptions.ServiceName)
                    .AddSource("WCF.Custom.Telemetry");
                })
                .WithMetrics(metrics =>
                {
                    // Configure metrics collection
                    metrics
                        .EnableHttpServerMetrics()
                        .EnableCustomMetrics()
                        .EnableRuntimeMetrics();
                })
                .Build());

            Debug.WriteLine($"Observability initialized for service: {configOptions.ServiceName}");
        }
      

        protected void Application_End(object sender, EventArgs e)
        {
            TelemetryConfiguration.Shutdown();
            Debug.WriteLine("Observability shut down successfully");
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            // This will fire for every request - use for debugging module issues
            Debug.WriteLine($"Application_BeginRequest: {Request.RawUrl}");
        }
    }
}
