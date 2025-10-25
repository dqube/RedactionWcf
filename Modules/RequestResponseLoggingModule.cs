using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RedactionWcf.Modules
{
    /// <summary>
    /// HTTP Module for logging requests and responses with correlation tracking
    /// Handles both WCF services and Web API controllers
    /// Logs request and response as a single combined entry
    /// </summary>
    public class RequestResponseLoggingModule : IHttpModule
    {
        private const string CorrelationIdHeader = "X-Correlation-Id";
        private const string ConsumerIdHeader = "X-Consumer-Id";
        private const string UserIdHeader = "X-User-Id";

        public void Init(HttpApplication context)
        {
            context.BeginRequest += OnBeginRequest;
            context.EndRequest += OnEndRequest;
            context.Error += OnError;
        }

        private void OnBeginRequest(object sender, EventArgs e)
        {
            var application = (HttpApplication)sender;
            var context = application.Context;
            var request = context.Request;

            try
            {
                // Skip logging for HTML pages, static resources, and landing pages
                if (ShouldSkipLogging(request))
                {
                    return;
                }

                // Extract correlation information with proper priority:
                // 1. HTTP Headers (highest priority)
                // 2. Request Body (fallback)
                // 3. Auto-generate (if not found anywhere)
                
                string correlationId = GetHeaderValue(request, CorrelationIdHeader);
                string consumerId = GetHeaderValue(request, ConsumerIdHeader);
                string userId = GetHeaderValue(request, UserIdHeader);

                // If any IDs are missing from headers, try to extract from request body
                if (string.IsNullOrEmpty(correlationId) || string.IsNullOrEmpty(consumerId) || string.IsNullOrEmpty(userId))
                {
                    ExtractFromRequestBody(request, ref correlationId, ref consumerId, ref userId);
                }

                // If CorrelationId is still not found, generate a new one
                if (string.IsNullOrEmpty(correlationId))
                {
                    correlationId = Guid.NewGuid().ToString();
                }

                // Capture request details
                var requestInfo = new RequestInfo
                {
                    Timestamp = DateTime.UtcNow,
                    Method = request.HttpMethod,
                    Path = request.RawUrl,
                    QueryString = request.QueryString.ToString(),
                    ContentType = request.ContentType,
                    Headers = GetSafeHeaders(request.Headers),
                    Body = SanitizeBody(CaptureRequestBody(request))
                };

                // Store context information for the request
                var requestContext = new RequestResponseContext
                {
                    CorrelationId = correlationId,
                    ConsumerId = consumerId,
                    UserId = userId,
                    RequestTime = DateTime.UtcNow,
                    RequestInfo = requestInfo
                };

                context.Items["RequestContext"] = requestContext;

                // Add correlation ID to response headers for tracking
                context.Response.Headers.Add(CorrelationIdHeader, correlationId);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error in RequestResponseLoggingModule.OnBeginRequest: {ex}");
                Debug.WriteLine($"ERROR in OnBeginRequest: {ex}");
            }
        }

        private void OnEndRequest(object sender, EventArgs e)
        {
            var application = (HttpApplication)sender;
            var context = application.Context;

            try
            {
                if (context.Items["RequestContext"] is RequestResponseContext requestContext)
                {
                    var response = context.Response;
                    var duration = DateTime.UtcNow - requestContext.RequestTime;

                    // Capture response details
                    var responseInfo = new ResponseInfo
                    {
                        Timestamp = DateTime.UtcNow,
                        StatusCode = response.StatusCode,
                        StatusDescription = response.StatusDescription,
                        ContentType = response.ContentType,
                        Headers = GetSafeHeaders(response.Headers),
                        Body = SanitizeBody(CaptureResponseBody(context))
                    };

                    // Log combined request/response
                    LogRequestResponse(requestContext, responseInfo, duration);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error in RequestResponseLoggingModule.OnEndRequest: {ex}");
                Debug.WriteLine($"ERROR in OnEndRequest: {ex}");
            }
        }

        private void OnError(object sender, EventArgs e)
        {
            var application = (HttpApplication)sender;
            var context = application.Context;
            var exception = context.Server.GetLastError();

            try
            {
                if (context.Items["RequestContext"] is RequestResponseContext requestContext)
                {
                    var duration = DateTime.UtcNow - requestContext.RequestTime;
                    LogError(requestContext, exception, duration);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error in RequestResponseLoggingModule.OnError: {ex}");
                Debug.WriteLine($"ERROR in OnError: {ex}");
            }
        }

        private string GetHeaderValue(HttpRequest request, string headerName)
        {
            return request.Headers[headerName];
        }

        private void ExtractFromRequestBody(HttpRequest request, ref string correlationId, ref string consumerId, ref string userId)
        {
            try
            {
                if (request.ContentType != null && 
                    request.ContentType.Contains("application/json") && 
                    request.InputStream.CanSeek)
                {
                    long originalPosition = request.InputStream.Position;
                    request.InputStream.Position = 0;

                    using (var reader = new StreamReader(request.InputStream, Encoding.UTF8, true, 1024, true))
                    {
                        var body = reader.ReadToEnd();
                        
                        if (!string.IsNullOrEmpty(body))
                        {
                            var jsonObject = JObject.Parse(body);
                            
                            // Extract CorrelationId - check root level first
                            if (string.IsNullOrEmpty(correlationId))
                            {
                                correlationId = jsonObject["CorrelationId"]?.ToString() ?? 
                                              jsonObject["correlationId"]?.ToString();
                                
                                // If not found at root, check nested header/headers object
                                if (string.IsNullOrEmpty(correlationId))
                                {
                                    correlationId = jsonObject["header"]?["CorrelationId"]?.ToString() ?? 
                                                  jsonObject["header"]?["correlationId"]?.ToString() ??
                                                  jsonObject["headers"]?["CorrelationId"]?.ToString() ?? 
                                                  jsonObject["headers"]?["correlationId"]?.ToString();
                                }
                            }
                            
                            // Extract ConsumerId - check root level first
                            if (string.IsNullOrEmpty(consumerId))
                            {
                                consumerId = jsonObject["ConsumerId"]?.ToString() ?? 
                                           jsonObject["consumerId"]?.ToString();
                                
                                // If not found at root, check nested header/headers object
                                if (string.IsNullOrEmpty(consumerId))
                                {
                                    consumerId = jsonObject["header"]?["ConsumerId"]?.ToString() ?? 
                                               jsonObject["header"]?["consumerId"]?.ToString() ??
                                               jsonObject["headers"]?["ConsumerId"]?.ToString() ?? 
                                               jsonObject["headers"]?["consumerId"]?.ToString();
                                }
                            }
                            
                            // Extract UserId - check root level first
                            if (string.IsNullOrEmpty(userId))
                            {
                                userId = jsonObject["UserId"]?.ToString() ?? 
                                       jsonObject["userId"]?.ToString();
                                
                                // If not found at root, check nested header/headers object
                                if (string.IsNullOrEmpty(userId))
                                {
                                    userId = jsonObject["header"]?["UserId"]?.ToString() ?? 
                                           jsonObject["header"]?["userId"]?.ToString() ??
                                           jsonObject["headers"]?["UserId"]?.ToString() ?? 
                                           jsonObject["headers"]?["userId"]?.ToString();
                                }
                            }
                        }
                    }

                    request.InputStream.Position = originalPosition;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to extract IDs from request body: {ex.Message}");
            }
        }

        private string CaptureRequestBody(HttpRequest request)
        {
            try
            {
                if (request.InputStream.CanSeek)
                {
                    long originalPosition = request.InputStream.Position;
                    request.InputStream.Position = 0;

                    using (var reader = new StreamReader(request.InputStream, Encoding.UTF8, true, 1024, true))
                    {
                        var body = reader.ReadToEnd();
                        request.InputStream.Position = originalPosition;
                        return body;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to capture request body: {ex.Message}");
            }

            return null;
        }

        private string CaptureResponseBody(HttpContext context)
        {
            try
            {
                // For capturing response body, we need to use a response filter
                // This is a simplified version - in production, you'd want to use a MemoryStream wrapper
                if (context.Items["ResponseBody"] is string responseBody)
                {
                    return responseBody;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to capture response body: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Logs combined request and response as a single log entry
        /// </summary>
        private void LogRequestResponse(RequestResponseContext context, ResponseInfo responseInfo, TimeSpan duration)
        {
            var combinedLog = new
            {
                LogType = "RequestResponse",
                CorrelationId = context.CorrelationId,
                ConsumerId = context.ConsumerId,
                UserId = context.UserId,
                Duration = new
                {
                    TotalMilliseconds = duration.TotalMilliseconds,
                    Seconds = duration.TotalSeconds
                },
                Request = new
                {
                    Timestamp = context.RequestInfo.Timestamp,
                    Method = context.RequestInfo.Method,
                    Path = context.RequestInfo.Path,
                    QueryString = context.RequestInfo.QueryString,
                    ContentType = context.RequestInfo.ContentType,
                    Headers = context.RequestInfo.Headers,
                    Body = context.RequestInfo.Body
                },
                Response = new
                {
                    Timestamp = responseInfo.Timestamp,
                    StatusCode = responseInfo.StatusCode,
                    StatusDescription = responseInfo.StatusDescription,
                    ContentType = responseInfo.ContentType,
                    Headers = responseInfo.Headers,
                    Body = responseInfo.Body
                }
            };

            string logMessage = JsonConvert.SerializeObject(combinedLog, Formatting.Indented);
            
            // Log to trace
            Trace.TraceInformation(logMessage);

            // Also log to Debug for development
            Debug.WriteLine("=== REQUEST/RESPONSE LOG ===");
            Debug.WriteLine(logMessage);
            Debug.WriteLine("============================");
        }

        /// <summary>
        /// Logs error with request details
        /// </summary>
        private void LogError(RequestResponseContext context, Exception exception, TimeSpan duration)
        {
            var errorLog = new
            {
                LogType = "Error",
                CorrelationId = context.CorrelationId,
                ConsumerId = context.ConsumerId,
                UserId = context.UserId,
                Duration = new
                {
                    TotalMilliseconds = duration.TotalMilliseconds,
                    Seconds = duration.TotalSeconds
                },
                Request = new
                {
                    Timestamp = context.RequestInfo.Timestamp,
                    Method = context.RequestInfo.Method,
                    Path = context.RequestInfo.Path,
                    QueryString = context.RequestInfo.QueryString,
                    ContentType = context.RequestInfo.ContentType,
                    Headers = context.RequestInfo.Headers,
                    Body = context.RequestInfo.Body
                },
                Error = new
                {
                    Timestamp = DateTime.UtcNow,
                    Message = exception.Message,
                    Type = exception.GetType().Name,
                    StackTrace = exception.StackTrace,
                    InnerException = exception.InnerException?.Message
                }
            };

            string logMessage = JsonConvert.SerializeObject(errorLog, Formatting.Indented);
            
            // Log to trace
            Trace.TraceError(logMessage);

            // Also log to Debug for development
            Debug.WriteLine("=== ERROR LOG ===");
            Debug.WriteLine(logMessage);
            Debug.WriteLine("=================");
        }

        private object GetSafeHeaders(System.Collections.Specialized.NameValueCollection headers)
        {
            var safeHeaders = new System.Collections.Generic.Dictionary<string, string>();
            var sensitiveHeaders = new[] { "Authorization", "Cookie", "Set-Cookie", "X-API-Key" };

            foreach (string key in headers.AllKeys)
            {
                if (key != null)
                {
                    if (Array.IndexOf(sensitiveHeaders, key) >= 0)
                    {
                        safeHeaders[key] = "***REDACTED***";
                    }
                    else
                    {
                        safeHeaders[key] = headers[key];
                    }
                }
            }

            return safeHeaders;
        }

        private string SanitizeBody(string body)
        {
            if (string.IsNullOrEmpty(body))
                return body;

            try
            {
                // Attempt to parse as JSON and redact sensitive fields
                var jsonObject = JObject.Parse(body);
                var sensitiveFields = new[] { "password", "Password", "token", "Token", "secret", "Secret", "apiKey", "ApiKey" };

                foreach (var field in sensitiveFields)
                {
                    if (jsonObject[field] != null)
                    {
                        jsonObject[field] = "***REDACTED***";
                    }
                }

                return jsonObject.ToString(Formatting.None);
            }
            catch
            {
                // If not JSON or parsing fails, return as is (or truncate if too large)
                return body.Length > 10000 ? body.Substring(0, 10000) + "... [TRUNCATED]" : body;
            }
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        /// <summary>
        /// Internal class to hold request and response context information
        /// </summary>
        private class RequestResponseContext
        {
            public string CorrelationId { get; set; }
            public string ConsumerId { get; set; }
            public string UserId { get; set; }
            public DateTime RequestTime { get; set; }
            public RequestInfo RequestInfo { get; set; }
        }

        /// <summary>
        /// Request details
        /// </summary>
        private class RequestInfo
        {
            public DateTime Timestamp { get; set; }
            public string Method { get; set; }
            public string Path { get; set; }
            public string QueryString { get; set; }
            public string ContentType { get; set; }
            public object Headers { get; set; }
            public string Body { get; set; }
        }

        /// <summary>
        /// Response details
        /// </summary>
        private class ResponseInfo
        {
            public DateTime Timestamp { get; set; }
            public int StatusCode { get; set; }
            public string StatusDescription { get; set; }
            public string ContentType { get; set; }
            public object Headers { get; set; }
            public string Body { get; set; }
        }

        /// <summary>
        /// Determines if the request should be excluded from logging
        /// Excludes: HTML pages, static resources, landing pages, MVC views
        /// </summary>
        private bool ShouldSkipLogging(HttpRequest request)
        {
            var path = request.RawUrl.ToLowerInvariant();
            var contentType = request.ContentType?.ToLowerInvariant() ?? string.Empty;
            var acceptHeader = request.Headers["Accept"]?.ToLowerInvariant() ?? string.Empty;

            // Skip if requesting HTML pages (Accept header contains text/html)
            if (acceptHeader.Contains("text/html"))
            {
                return true;
            }

            // Skip static file extensions
            var staticExtensions = new[] { ".css", ".js", ".jpg", ".jpeg", ".png", ".gif", ".ico", 
                                          ".woff", ".woff2", ".ttf", ".eot", ".svg", ".map", ".html", ".htm" };
            
            if (staticExtensions.Any(ext => path.EndsWith(ext)))
            {
                return true;
            }

            // Skip common MVC/Web Forms landing pages and help pages
            var excludedPaths = new[] 
            { 
                "/",                    // Root landing page
                "/home",                // Home controller
                "/home/index",          // Home index action
                "/help",                // Help pages
                "/areas/helppage",      // Help page area
                "/content/",            // Content folder
                "/scripts/",            // Scripts folder
                "/bundles/",            // Script bundles
                "/fonts/",              // Font files
                "/images/",             // Image files
                "/favicon.ico",         // Favicon
                "/__browserlink",       // Browser Link (Visual Studio)
                "/trace.axd",           // Trace handler
                "/glimpse.axd"          // Glimpse diagnostics
            };

            if (excludedPaths.Any(excluded => path.StartsWith(excluded) || path.Contains(excluded)))
            {
                return true;
            }

            // Skip if response content type is HTML (for responses already being sent)
            if (contentType.Contains("text/html"))
            {
                return true;
            }

            // Log only API requests (WCF services and Web API)
            // Include: .svc endpoints, /api/ routes, or JSON/XML content types
            bool isApiRequest = path.Contains(".svc") || 
                               path.StartsWith("/api/") ||
                               acceptHeader.Contains("application/json") ||
                               acceptHeader.Contains("application/xml") ||
                               contentType.Contains("application/json") ||
                               contentType.Contains("application/xml");

            // Skip if it's NOT an API request
            return !isApiRequest;
        }
    }
}
