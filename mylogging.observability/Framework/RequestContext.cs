#if NET48_OR_GREATER

using System;
using System.Web;

namespace mylogging.observability.Framework
{
    /// <summary>
    /// Context information for tracking requests across the application
    /// Stored in HttpContext.Items for internal use only (not serialized)
    /// </summary>
    public class RequestContext
    {
        /// <summary>
        /// Gets or sets the correlation ID for the request
        /// </summary>
        public string CorrelationId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the consumer ID for the request
        /// </summary>
        public string ConsumerId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the user ID for the request
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the request timestamp
        /// </summary>
        public DateTime RequestTime { get; set; }

        /// <summary>
        /// Gets or sets the request path
        /// </summary>
        public string RequestPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the HTTP method
        /// </summary>
        public string HttpMethod { get; set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="RequestContext"/> class
        /// </summary>
        public RequestContext()
        {
            RequestTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Get the current request context from HttpContext.Items
        /// </summary>
        public static RequestContext? Current
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
#endif