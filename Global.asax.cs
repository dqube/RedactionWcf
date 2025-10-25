using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using System.Diagnostics;

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
            
            // Verify HTTP Module is configured
            Debug.WriteLine("Application Started - HTTP Modules should be loading...");
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            // This will fire for every request - use for debugging module issues
            Debug.WriteLine($"Application_BeginRequest: {Request.RawUrl}");
        }
    }
}
