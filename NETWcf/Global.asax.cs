using Microsoft.Extensions.DependencyInjection;
using mylogging.observability.Common;
using mylogging.observability.Framework;
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
            
            // Load observability configuration
            var settings = (ObservabilitySettings)ConfigurationManager.GetSection("observability");
            var configOptions = settings?.ToOptions() ?? new ObservabilityOptions();
            
            // Configure request/response logging from options

            // Initialize OpenTelemetry with options
            WcfTelemetryConfiguration.Initialize(configOptions, builder =>
            {
                builder

                    // Add console exporter to see the logged bodies
                    .AddConsoleExporter(options =>
                    {
                        options.Targets = OpenTelemetry.Exporter.ConsoleExporterOutputTargets.Console;
                    })
                    .AddSource($"{configOptions.ServiceName}.WCF"); // Updated to match the valid source name
            });
        }
      

        protected void Application_End(object sender, EventArgs e)
        {
            WcfTelemetryConfiguration.Shutdown();
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            // This will fire for every request - use for debugging module issues
            Debug.WriteLine($"Application_BeginRequest: {Request.RawUrl}");
        }
    }
}
