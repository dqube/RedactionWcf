using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using System.Diagnostics;
using System.ServiceModel;
using System.ServiceModel.Description;
using RedactionWcf.Infrastructure;

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
            
            // Register WCF logging behavior
            RegisterWcfLoggingBehavior();
            
            // Verify HTTP Module is configured
            Debug.WriteLine("Application Started - HTTP Modules should be loading...");
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            // This will fire for every request - use for debugging module issues
            Debug.WriteLine($"Application_BeginRequest: {Request.RawUrl}");
        }

        private void RegisterWcfLoggingBehavior()
        {
            try
            {
                // Hook into service host creation to add the logging behavior
                // This is done via a custom service host factory or behavior configuration
                Debug.WriteLine("WCF Logging Behavior registration attempted");
                
                // Note: Since we're using WebServiceHostFactory, we need to use a custom factory
                // or configure via Web.config. The inspector will be added via custom factory below.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error registering WCF logging behavior: {ex.Message}");
            }
        }
    }
}
