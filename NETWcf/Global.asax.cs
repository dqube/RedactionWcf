using mylogging.observability.Framework;
using OpenTelemetry.Trace;
using System;
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

            ConfigureRequestResponseLogging();

            // Initialize OpenTelemetry
            WcfTelemetryConfiguration.Initialize(builder =>
            {
                builder
                    // Add console exporter to see the logged bodies
                    .AddConsoleExporter(options =>
                    {
                        options.Targets = OpenTelemetry.Exporter.ConsoleExporterOutputTargets.Console;
                    });

                    // Add OTLP exporter
                    //.AddOtlpExporter(options =>
                    //{
                    //    options.Endpoint = new Uri("http://localhost:4317");
                    //    options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    //});
            });
        }
        private void ConfigureRequestResponseLogging()
        {
            // Enable/disable request body logging
            WcfTelemetryHttpModule.LogRequestBody = true;

            // Enable/disable response body logging
            WcfTelemetryHttpModule.LogResponseBody = true;

            // Set maximum size for logged bodies (in characters)
            // Bodies larger than this will be truncated
            WcfTelemetryHttpModule.MaxBodyLogSize = 10000; // 10KB

            // Enable/disable sensitive data sanitization
            // When enabled, fields like "password", "token", etc. are redacted
            WcfTelemetryHttpModule.SanitizeSensitiveData = true;
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
