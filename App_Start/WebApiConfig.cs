using RedactionWcf.Handler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web.Http;

namespace RedactionWcf
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Add Web API message handler for response body capture
            config.MessageHandlers.Add(new WebApiLoggingHandler());

            // Web API configuration and services

            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }
    }
}
