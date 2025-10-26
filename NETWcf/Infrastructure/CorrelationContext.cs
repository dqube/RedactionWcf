using System;
using System.Web;

namespace RedactionWcf.Infrastructure
{
    /// <summary>
    /// Helper class to access correlation context from both WCF services and Web API controllers
    /// </summary>
    public static class CorrelationContext
    {
        private const string CorrelationIdKey = "CorrelationId";
        private const string ConsumerIdKey = "ConsumerId";
        private const string UserIdKey = "UserId";

        /// <summary>
        /// Get the correlation ID for the current request
        /// </summary>
        public static string CorrelationId
        {
            get { return HttpContext.Current?.Items[CorrelationIdKey]?.ToString(); }
        }

        /// <summary>
        /// Get the consumer ID for the current request
        /// </summary>
        public static string ConsumerId
        {
            get { return HttpContext.Current?.Items[ConsumerIdKey]?.ToString(); }
        }

        /// <summary>
        /// Get the user ID for the current request
        /// </summary>
        public static string UserId
        {
            get { return HttpContext.Current?.Items[UserIdKey]?.ToString(); }
        }

        /// <summary>
        /// Check if correlation context is available
        /// </summary>
        public static bool IsAvailable
        {
            get { return HttpContext.Current != null && HttpContext.Current.Items["RequestContext"] != null; }
        }
    }
}
