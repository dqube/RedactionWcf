using System;
using System.Web;

namespace RedactionWcf.Infrastructure
{
    /// <summary>
    /// Helper class to access correlation context from both WCF services and Web API controllers
    /// </summary>
    public static class ContextProvider
    {
        private const string CorrelationIdKey = "CorrelationId";
        private const string ConsumerIdKey = "ConsumerId";
        private const string UserIdKey = "UserId";
        private const string ClassNameKey = "ClassName";
        private const string OperationNameKey = "OperationName";

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
        /// Get the class name (service/controller name) for the current request
        /// </summary>
        public static string ClassName
        {
            get { return HttpContext.Current?.Items[ClassNameKey]?.ToString(); }
        }

        /// <summary>
        /// Get the operation name (method/action name) for the current request
        /// </summary>
        public static string OperationName
        {
            get { return HttpContext.Current?.Items[OperationNameKey]?.ToString(); }
        }

        /// <summary>
        /// Check if correlation context is available
        /// </summary>
        public static bool IsAvailable
        {
            get { return HttpContext.Current != null && HttpContext.Current.Items["RequestContext"] != null; }
        }

        /// <summary>
        /// Get all correlation context values as a formatted string
        /// </summary>
        public static string GetContextInfo()
        {
            if (!IsAvailable)
                return "Correlation context not available";

            return $"CorrelationId={CorrelationId}, ConsumerId={ConsumerId}, UserId={UserId}, ClassName={ClassName}, OperationName={OperationName}";
        }
    }
}
