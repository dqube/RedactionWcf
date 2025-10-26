using System;
using System.Web;

namespace RedactionWcf.Infrastructure
{
    /// <summary>
    /// Context information for tracking requests across the application
    /// Stored in HttpContext.Items for internal use only (not serialized)
    /// </summary>
    public class RequestContext
    {
        public string CorrelationId { get; set; }
        public string ConsumerId { get; set; }
        public string UserId { get; set; }
        public DateTime RequestTime { get; set; }
        public string RequestPath { get; set; }
        public string HttpMethod { get; set; }

        public RequestContext()
        {
            RequestTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Get the current request context from HttpContext.Items
        /// </summary>
        public static RequestContext Current
        {
            get
            {
                if (HttpContext.Current?.Items["RequestContext"] is RequestContext context)
                {
                    return context;
                }
                return null;
            }
            set
            {
                if (HttpContext.Current != null)
                {
                    HttpContext.Current.Items["RequestContext"] = value;
                }
            }
        }
    }
}
