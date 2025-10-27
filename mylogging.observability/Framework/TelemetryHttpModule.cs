#if NET48_OR_GREATER

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Xml;
using System.Xml.Linq;

namespace mylogging.observability.Framework
{
    /// <summary>
    /// Custom HTTP Module for OpenTelemetry WCF instrumentation
    /// Provides full distributed tracing capabilities for WCF services with request/response logging
    /// </summary>
    public class TelemetryHttpModule : IHttpModule
    {
        // Static logger instance (initialized once for the entire application)
        private static readonly ILogger _logger;
        private static ActivitySource? _activitySource;
        private const string RequestStartTimeKey = "WcfTelemetry.RequestStartTime";
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;
        private static string ActivityKey => $"{WcfTelemetryConfiguration.Options?.ServiceName ?? "WcfTelemetry"}.Activity";
        private const string RequestBodyKey = "WcfTelemetry.RequestBody";
        private const string ResponseFilterKey = "WcfTelemetry.ResponseFilter";
        private const string ResponseBodyKey = "WcfTelemetry.ResponseBody";

        static TelemetryHttpModule()
        {
            // Initialize logger factory
            var loggerFactory = LoggerFactory.Create(builder =>
            {               
                builder.SetMinimumLevel(LogLevel.Information);
            });

            _logger = loggerFactory.CreateLogger<TelemetryHttpModule>();
        }

        private static ActivitySource ActivitySource
        {
            get
            {
                if (_activitySource == null)
                {
                    var serviceName = WcfTelemetryConfiguration.Options?.ServiceName ?? "WcfTelemetry";
                    _activitySource = new ActivitySource($"{serviceName}.WCF", "1.0.0");
                }
                return _activitySource;
            }
        }

        


        // Alternative header names to check
        private static string[] CorrelationIdHeaders => WcfTelemetryConfiguration.Options?.RequestResponseLogging.CorrelationIdHeaders?.ToArray() ?? new[]
        {
                "X-Correlation-Id",
                "CorrelationId",
                "x-correlation-id",
                "correlationId",
                "Correlation-Id",
                "correlation-id"
            };

        private static string[] ConsumerIdHeaders => WcfTelemetryConfiguration.Options?.RequestResponseLogging.ConsumerIdHeaders?.ToArray() ?? new[]
        {
                "X-Consumer-Id",
                "ConsumerId",
                "x-consumer-id",
                "consumerId",
                "Consumer-Id",
                "consumer-id"
            };

        private static string[] UserIdHeaders => WcfTelemetryConfiguration.Options?.RequestResponseLogging.UserIdHeaders?.ToArray() ?? new[]
        {
                "X-User-Id",
                "UserId",
                "x-user-id",
                "userId",
                "User-Id",
                "user-id"
            };

        

        /// <summary>
        /// Initializes the HTTP module and hooks into application events
        /// </summary>
        /// <param name="context">The HTTP application context</param>
        public void Init(HttpApplication context)
        {
            context.BeginRequest += OnBeginRequest;
            context.EndRequest += OnEndRequest;
            context.Error += OnError;
        }

        private void OnBeginRequest(object sender, EventArgs e)
        {
            var context = ((HttpApplication)sender).Context;

            try
            {
                // Get options from WcfTelemetryConfiguration
                var options = WcfTelemetryConfiguration.Options;

                // Check if path should be excluded based on configuration
                if (options?.RequestResponseLogging?.ExcludePaths != null)
                {
                    var path = context.Request.RawUrl.ToLowerInvariant();
                    if (options.RequestResponseLogging.ExcludePaths.Any(excluded =>
                        path.Contains(excluded.ToLowerInvariant())))
                    {
                        return; // Skip logging for excluded paths
                    }
                }
                
                // Parse ClassName and OperationName from path early
                string? className = null;
                string? operationName = null;
                ParseClassAndOperationName(context.Request.RawUrl, out className, out operationName);
                if (string.IsNullOrEmpty(operationName))
                {
                    operationName = className;
                }
                
                // Extract trace context from incoming request
                var parentContext = Propagator.Extract(default, context.Request, ExtractTraceContext);
                Baggage.Current = parentContext.Baggage;

                // Capture request body before it's consumed
                string? requestBody = null;
                if (options!.EnableRequestResponseLogging && context.Request.InputStream.CanRead)
                {
                    requestBody = CaptureRequestBody(context.Request);
                    context.Items[RequestBodyKey] = requestBody;
                }
                
                // Extract correlation information from headers
                string? correlationId = GetHeaderValueWithFallback(context.Request, CorrelationIdHeaders);
                string? consumerId = GetHeaderValueWithFallback(context.Request, ConsumerIdHeaders);
                string? userId = GetHeaderValueWithFallback(context.Request, UserIdHeaders);

                if (string.IsNullOrEmpty(consumerId) || string.IsNullOrEmpty(userId))
                {
                    // Extract from body if needed (only if requestBody is not null)
                    if (!string.IsNullOrEmpty(requestBody))
                    {
                        ExtractFromRequestBody(requestBody, context.Request.ContentType, ref correlationId, ref consumerId, ref userId);
                    }
                }
                
                // Store correlation info in HttpContext.Items for ContextProvider
                if (!string.IsNullOrEmpty(correlationId))
                    context.Items["CorrelationId"] = correlationId;
                if (!string.IsNullOrEmpty(consumerId))
                    context.Items["ConsumerId"] = consumerId;
                if (!string.IsNullOrEmpty(userId))
                    context.Items["UserId"] = userId;
                if (!string.IsNullOrEmpty(className))
                    context.Items["ClassName"] = className;
                if (!string.IsNullOrEmpty(operationName))
                    context.Items["OperationName"] = operationName;

                // Start activity with extracted context
                // IMPORTANT: Use Activity.Current if ActivitySource returns null (fallback mechanism)
                var activity = ActivitySource.StartActivity(
                    "WCF.Request",
                    ActivityKind.Server,
                    parentContext.ActivityContext);

                // If activity is null, check if there's a current activity or create a manual one
                if (activity == null)
                {
                    Trace.TraceWarning($"ActivitySource.StartActivity returned null. ActivitySource name: {ActivitySource.Name}. " +
                                     "Ensure OpenTelemetry TracerProvider is configured with AddSource(\"{ActivitySource.Name}\")");
                    
                    // Fallback: Try to use existing Activity.Current or create a new one manually
                    if (Activity.Current != null)
                    {
                        activity = Activity.Current;
                    }
                    else
                    {
                        // Manual activity creation as last resort
                        activity = new Activity("WCF.Request");
                        activity.SetParentId(parentContext.ActivityContext.TraceId, parentContext.ActivityContext.SpanId, parentContext.ActivityContext.TraceFlags);
                        activity.Start();
                    }
                }

                if (activity != null)
                {
                    // Set Activity.Current so it can be accessed in service code
                    Activity.Current = activity;

                    // Set standard attributes
                    activity.SetTag("rpc.system", options?.ServiceName ?? "WcfTelemetry");
                    activity.SetTag("rpc.service", options?.ServiceName ?? "WcfTelemetry");
                    activity.SetTag("rpc.method", operationName);
                    activity.SetTag("http.method", context.Request.HttpMethod);
                    activity.SetTag("http.url", context.Request.Url.ToString());
                    activity.SetTag("http.target", context.Request.RawUrl);
                    activity.SetTag("http.host", context.Request.Url.Host);
                    activity.SetTag("http.scheme", context.Request.Url.Scheme);
                    activity.SetTag("net.host.name", context.Request.Url.Host);
                    activity.SetTag("net.host.port", context.Request.Url.Port);

                    // Add correlation IDs to activity
                    if (!string.IsNullOrEmpty(correlationId))
                        activity.SetTag("correlation.id", correlationId);
                    if (!string.IsNullOrEmpty(consumerId))
                        activity.SetTag("consumer.id", consumerId);
                    if (!string.IsNullOrEmpty(userId))
                        activity.SetTag("user.id", userId);

                    if (!string.IsNullOrEmpty(context.Request.UserAgent))
                        activity.SetTag("http.user_agent", context.Request.UserAgent);

                    if (!string.IsNullOrEmpty(context.Request.UserHostAddress))
                        activity.SetTag("net.peer.ip", context.Request.UserHostAddress);

                    // Log request body size
                    if (requestBody != null)
                    {
                        activity.SetTag("http.request_content_length", requestBody.Length);
                    }

                    // Store activity in context for later retrieval
                    context.Items[ActivityKey] = activity;

                    // Install response filter to capture response body
                    if (options!.EnableRequestResponseLogging && context.Response.Filter != null)
                    {
                        var responseFilter = new ResponseCaptureFilter(context.Response.Filter, context);
                        context.Response.Filter = responseFilter;
                        context.Items[ResponseFilterKey] = responseFilter;
                    }

                    // Add custom enrichment hook
                    EnrichActivity(activity, context, "OnBeginRequest");
                }
                else
                {
                    Trace.TraceError("Failed to create activity even with fallback mechanisms. OpenTelemetry may not be properly configured.");
                }
            }
            catch (Exception ex)
            {
                // Log error but don't break the request pipeline
                Trace.TraceError($"WcfTelemetryHttpModule.OnBeginRequest error: {ex}");
            }
        }
        /// <summary>
        /// Extracts ClassName and OperationName from the request path
        /// For WCF: /ServiceName.svc/operation => ClassName=ServiceName, OperationName=operation
        /// For Web API: /api/controller/action => ClassName=controller, OperationName=action
        /// </summary>
        private void ParseClassAndOperationName(string path, out string? className, out string? operationName)
        {
            className = null;
            operationName = null;

            try
            {
                if (string.IsNullOrEmpty(path))
                    return;

                // Remove query string if present
                var pathWithoutQuery = path.Split('?')[0];

                // Split path into segments
                var segments = pathWithoutQuery.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                if (segments.Length == 0)
                    return;

                // Handle WCF service path: /ServiceName.svc/operation
                if (pathWithoutQuery.Contains(".svc"))
                {
                    for (int i = 0; i < segments.Length; i++)
                    {
                        if (segments[i].EndsWith(".svc", StringComparison.OrdinalIgnoreCase))
                        {
                            // Extract service name without .svc extension
                            className = segments[i].Substring(0, segments[i].Length - 4);

                            // Operation name is the next segment
                            if (i + 1 < segments.Length)
                            {
                                operationName = segments[i + 1];
                            }
                            break;
                        }
                    }
                }
                // Handle Web API path: /api/controller/action
                else if (pathWithoutQuery.ToLowerInvariant().StartsWith("/api/"))
                {
                    // segments[0] = "api", segments[1] = controller, segments[2] = action
                    if (segments.Length >= 2)
                    {
                        className = segments[1]; // controller name
                    }
                    if (segments.Length >= 3)
                    {
                        operationName = segments[2]; // action name
                    }
                }
                // Handle generic controller/action pattern
                else if (segments.Length >= 2)
                {
                    className = segments[0];
                    operationName = segments[1];
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing class and operation name from path '{path}': {ex.Message}");
            }
        }


        private void OnEndRequest(object sender, EventArgs e)
        {
            var context = ((HttpApplication)sender).Context;
            var activity = context.Items[ActivityKey] as Activity;

            if (activity == null)
                return;

            try
            {
                // Set response attributes
                activity.SetTag("http.status_code", context.Response.StatusCode);               

                // Determine activity status based on response
                if (context.Response.StatusCode >= 400)
                {
                    activity.SetStatus(ActivityStatusCode.Error, $"HTTP {context.Response.StatusCode}");
                }
                else
                {
                    activity.SetStatus(ActivityStatusCode.Ok);
                }

                

                // Add custom enrichment hook
                EnrichActivity(activity, context, "OnEndRequest");
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.AddException(ex);
                Trace.TraceError($"WcfTelemetryHttpModule.OnEndRequest error: {ex}");
            }
            finally
            {
                // ✅ CRITICAL: Always dispose the activity, even if errors occurred
                try
                {
                    // Capture request/response for logging BEFORE disposing activity
                    var requestBody = context.Items[RequestBodyKey] as string;
                    var responseFilter = context.Items[ResponseFilterKey] as ResponseCaptureFilter;
                    var responseBody = responseFilter?.GetCapturedContent();

                    // Log the complete request/response
                    LogRequestResponse(context, activity, requestBody, responseBody);

                    // Stop the activity (marks end time and duration)
                    activity.Stop();

                    // Dispose the activity (releases resources)
                    activity.Dispose();

                    // Clear the activity from context to prevent double-disposal
                    context.Items.Remove(ActivityKey);

                    // Reset Activity.Current if it's our activity to prevent memory leaks
                    if (Activity.Current == activity)
                    {
                        Activity.Current = null;
                    }
                }
                catch (Exception disposeEx)
                {
                    // Log but don't throw - disposal errors shouldn't break the pipeline
                    Trace.TraceWarning($"Error disposing activity: {disposeEx.Message}");
                    _logger.LogWarning(disposeEx, "Error disposing activity");
                }
            }
        }

        private void OnError(object sender, EventArgs e)
        {
            var context = ((HttpApplication)sender).Context;
            var activity = context.Items[ActivityKey] as Activity;

            if (activity == null)
                return;

            try
            {
                var exception = context.Server.GetLastError();
                if (exception != null)
                {
                    activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                    activity.AddException(exception);

                    // Add exception details
                    activity.SetTag("error", true);
                    activity.SetTag("exception.type", exception.GetType().FullName);
                    activity.SetTag("exception.message", exception.Message);
                    activity.SetTag("exception.stacktrace", exception.StackTrace);                   

                    // Capture request body for error logging
                    var requestBody = context.Items[RequestBodyKey] as string;

                    // Log the error with full context
                    LogErrorDetails(context, activity, exception, requestBody);
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw - error handling shouldn't break the pipeline
                Trace.TraceError($"WcfTelemetryHttpModule.OnError error: {ex}");
                _logger.LogError(ex, "WcfTelemetryHttpModule.OnError error");
            }
            // ⚠️ IMPORTANT: Do NOT dispose activity here
            // OnEndRequest will be called after OnError and will handle disposal
        }
        private string? CaptureRequestBody(HttpRequest request)
        {
            try
            {
                if (request.InputStream == null || !request.InputStream.CanSeek)
                    return null;

                var position = request.InputStream.Position;
                request.InputStream.Position = 0;

                using (var reader = new StreamReader(
                    request.InputStream,
                    request.ContentEncoding ?? Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1024,
                    leaveOpen: true))
                {
                    var body = reader.ReadToEnd();
                    request.InputStream.Position = position;
                    return body;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Error capturing request body: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// Gets header value by checking multiple possible header names.
        /// Returns the first non-empty value found.
        /// </summary>
        private string? GetHeaderValueWithFallback(HttpRequest request, string[] headerNames)
        {
            foreach (var headerName in headerNames)
            {
                var value = request.Headers[headerName];
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            return null;
        }

        /// <summary>
        /// Extracts IDs from request body string (already captured)
        /// </summary>
        private void ExtractFromRequestBody(string body, string contentType, ref string? correlationId, ref string? consumerId, ref string? userId)
        {
            try
            {
                if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(contentType))
                {
                    return;
                }

                contentType = contentType.ToLowerInvariant();

                // Handle JSON content
                if (contentType.Contains("application/json"))
                {
                    ExtractFromJson(body, ref correlationId, ref consumerId, ref userId);
                }
                // Handle XML content
                else if (contentType.Contains("application/xml") || contentType.Contains("text/xml"))
                {
                    ExtractFromXml(body, ref correlationId, ref consumerId, ref userId);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to extract IDs from request body: {ex.Message}");
                Debug.WriteLine($"Warning: Failed to extract IDs from request body: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract correlation IDs from JSON request body
        /// </summary>
        private void ExtractFromJson(string body, ref string? correlationId, ref string? consumerId, ref string? userId)
        {
            try
            {
                var jsonObject = JObject.Parse(body);

                // Extract CorrelationId - check root level first with multiple name variations
                if (string.IsNullOrEmpty(correlationId))
                {
                    correlationId = GetJsonPropertyValue(jsonObject,
                        "CorrelationId", "correlationId", "Correlation-Id", "correlation-id",
                        "X-Correlation-Id", "x-correlation-id");

                    // If not found at root, check nested header/headers object
                    if (string.IsNullOrEmpty(correlationId))
                    {
                        var headerObj = jsonObject["header"] ?? jsonObject["headers"] ??
                                       jsonObject["Header"] ?? jsonObject["Headers"];

                        if (headerObj != null && headerObj.Type == JTokenType.Object)
                        {
                            correlationId = GetJsonPropertyValue((JObject)headerObj,
                                "CorrelationId", "correlationId", "Correlation-Id", "correlation-id",
                                "X-Correlation-Id", "x-correlation-id");
                        }
                    }
                }

                // Extract ConsumerId
                if (string.IsNullOrEmpty(consumerId))
                {
                    consumerId = GetJsonPropertyValue(jsonObject,
                        "ConsumerId", "consumerId", "Consumer-Id", "consumer-id",
                        "X-Consumer-Id", "x-consumer-id");

                    if (string.IsNullOrEmpty(consumerId))
                    {
                        var headerObj = jsonObject["header"] ?? jsonObject["headers"] ??
                                       jsonObject["Header"] ?? jsonObject["Headers"];

                        if (headerObj != null && headerObj.Type == JTokenType.Object)
                        {
                            consumerId = GetJsonPropertyValue((JObject)headerObj,
                                "ConsumerId", "consumerId", "Consumer-Id", "consumer-id",
                                "X-Consumer-Id", "x-consumer-id");
                        }
                    }
                }

                // Extract UserId
                if (string.IsNullOrEmpty(userId))
                {
                    userId = GetJsonPropertyValue(jsonObject,
                        "UserId", "userId", "User-Id", "user-id",
                        "X-User-Id", "x-user-id");

                    if (string.IsNullOrEmpty(userId))
                    {
                        var headerObj = jsonObject["header"] ?? jsonObject["headers"] ??
                                       jsonObject["Header"] ?? jsonObject["Headers"];

                        if (headerObj != null && headerObj.Type == JTokenType.Object)
                        {
                            userId = GetJsonPropertyValue((JObject)headerObj,
                                "UserId", "userId", "User-Id", "user-id",
                                "X-User-Id", "x-user-id");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to parse JSON body: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to get JSON property value by checking multiple property names
        /// </summary>
        private string? GetJsonPropertyValue(JObject jsonObject, params string[] propertyNames)
        {
            foreach (var propName in propertyNames)
            {
                var value = jsonObject[propName]?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            return null;
        }

        /// <summary>
        /// Extract correlation IDs from XML request body
        /// </summary>
        private void ExtractFromXml(string body, ref string? correlationId, ref string? consumerId, ref string? userId)
        {
            try
            {
                var doc = XDocument.Parse(body);
                var root = doc.Root;

                if (root == null)
                {
                    return;
                }

                // Extract CorrelationId
                if (string.IsNullOrEmpty(correlationId))
                {
                    correlationId = GetXmlElementValue(root,
                        "CorrelationId", "correlationId", "Correlation-Id", "correlation-id",
                        "X-Correlation-Id", "x-correlation-id");

                    if (string.IsNullOrEmpty(correlationId))
                    {
                        var headerElement = root.Element("Header") ?? root.Element("header") ??
                                          root.Element("Headers") ?? root.Element("headers");

                        if (headerElement != null)
                        {
                            correlationId = GetXmlElementValue(headerElement,
                                "CorrelationId", "correlationId", "Correlation-Id", "correlation-id",
                                "X-Correlation-Id", "x-correlation-id");
                        }
                    }
                }

                // Extract ConsumerId
                if (string.IsNullOrEmpty(consumerId))
                {
                    consumerId = GetXmlElementValue(root,
                        "ConsumerId", "consumerId", "Consumer-Id", "consumer-id",
                        "X-Consumer-Id", "x-consumer-id");

                    if (string.IsNullOrEmpty(consumerId))
                    {
                        var headerElement = root.Element("Header") ?? root.Element("header") ??
                                          root.Element("Headers") ?? root.Element("headers");

                        if (headerElement != null)
                        {
                            consumerId = GetXmlElementValue(headerElement,
                                "ConsumerId", "consumerId", "Consumer-Id", "consumer-id",
                                "X-Consumer-Id", "x-consumer-id");
                        }
                    }
                }

                // Extract UserId
                if (string.IsNullOrEmpty(userId))
                {
                    userId = GetXmlElementValue(root,
                        "UserId", "userId", "User-Id", "user-id",
                        "X-User-Id", "x-user-id");

                    if (string.IsNullOrEmpty(userId))
                    {
                        var headerElement = root.Element("Header") ?? root.Element("header") ??
                                          root.Element("Headers") ?? root.Element("headers");

                        if (headerElement != null)
                        {
                            userId = GetXmlElementValue(headerElement,
                                "UserId", "userId", "User-Id", "user-id",
                                "X-User-Id", "x-user-id");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to parse XML body: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to get XML element value
        /// </summary>
        private string? GetXmlElementValue(XElement parent, params string[] elementNames)
        {
            foreach (var elemName in elementNames)
            {
                var value = parent.Element(elemName)?.Value;
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            return null;
        }
        

        /// <summary>
        /// Determines if the request should be excluded from logging
        /// </summary>
        private bool ShouldSkipLogging(HttpRequest request)
        {
            var options = WcfTelemetryConfiguration.Options;

            // If no options configured, use default behavior
            if (options?.RequestResponseLogging == null)
            {
                return false;
            }

            var path = request.RawUrl.ToLowerInvariant();
            var contentType = request.ContentType?.ToLowerInvariant() ?? string.Empty;
            var acceptHeader = request.Headers["Accept"]?.ToLowerInvariant() ?? string.Empty;

            // Check excluded paths from configuration
            if (options.RequestResponseLogging.ExcludePaths != null && options.RequestResponseLogging.ExcludePaths.Any())
            {
                if (options.RequestResponseLogging.ExcludePaths.Any(excluded =>
                    path.Contains(excluded.ToLowerInvariant())))
                {
                    return true;
                }
            }
            else
            {
                // Use default excluded paths if none configured
                var excludedPaths = new[]
                {
                    "/home", "/home/index", "/help", "/areas/helppage", "/content/",
                    "/scripts/", "/bundles/", "/fonts/", "/images/", "/favicon.ico",
                    "/__browserlink", "/trace.axd", "/glimpse.axd"
                };

                if (excludedPaths.Any(excluded => path.StartsWith(excluded) || path.Contains(excluded)))
                {
                    return true;
                }
            }

            // Skip if requesting HTML pages
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

            // Skip if response content type is HTML
            if (contentType.Contains("text/html"))
            {
                return true;
            }

            // Log only API requests
            bool isApiRequest = path.Contains(".svc") ||
                               path.StartsWith("/api/") ||
                               acceptHeader.Contains("application/json") ||
                               acceptHeader.Contains("application/xml") ||
                               contentType.Contains("application/json") ||
                               contentType.Contains("application/xml");

            return !isApiRequest;
        }


        private IEnumerable<string> ExtractTraceContext(HttpRequest request, string key)
        {
            // Extract from HTTP headers
            var values = request.Headers.GetValues(key);
            return values ?? Enumerable.Empty<string>();
        }

        /// <summary>
        /// Checks if logging should occur based on configuration
        /// </summary>
        private bool ShouldLog(HttpContext context)
        {
            var options = WcfTelemetryConfiguration.Options;
            
            // Check if logging is enabled
            if (options?.EnableRequestResponseLogging == false)
                return false;
                
            // Check if path is excluded
            if (options?.RequestResponseLogging?.ExcludePaths != null)
            {
                var path = context.Request.RawUrl.ToLowerInvariant();
                if (options.RequestResponseLogging.ExcludePaths.Any(excluded =>
                    path.Contains(excluded.ToLowerInvariant())))
                {
                    return false;
                }
            }
            
            return true;
        }

        /// <summary>
        /// Logs successful request/response with all details
        /// </summary>
        private void LogRequestResponse(HttpContext context, Activity activity, string? requestBody, string? responseBody)
        {
            try
            {
                if (!ShouldLog(context))
                    return;

                var startTime = context.Items[RequestStartTimeKey] as DateTime? ?? DateTime.UtcNow;
                var executionTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                var correlationId = context.Items["CorrelationId"] as string ?? "N/A";
                var consumerId = context.Items["ConsumerId"] as string ?? "unknown";
                var userId = context.Items["UserId"] as string ?? "anonymous";
                var className = context.Items["ClassName"] as string ?? "Unknown";
                var operationName = context.Items["OperationName"] as string ?? className;

                // Prepare headers for logging
                var requestHeaders = new Dictionary<string, string>();
                foreach (var key in context.Request.Headers.AllKeys)
                {
                    if (key != null)
                    {
                        requestHeaders[key] = context.Request.Headers[key];
                    }
                }

                var responseHeaders = new Dictionary<string, string>();
                foreach (var key in context.Response.Headers.AllKeys)
                {
                    if (key != null)
                    {
                        responseHeaders[key] = context.Response.Headers[key];
                    }
                }

                // Create structured log object
                var logEntry = new
                {
                    LogType = "RequestResponse",
                    CorrelationId = correlationId,
                    ConsumerId = consumerId,
                    UserId = userId,
                    ClassName = className,
                    OperationName = operationName,
                    ExecutionTimeMs = executionTime,
                    Request = new
                    {
                        Timestamp = startTime,
                        Method = context.Request.HttpMethod,
                        Path = context.Request.RawUrl,
                        QueryString = context.Request.QueryString.ToString(),
                        ContentType = context.Request.ContentType,
                        Headers = requestHeaders,
                        Body = requestBody
                    },
                    Response = new
                    {
                        Timestamp = DateTime.UtcNow,
                        StatusCode = context.Response.StatusCode,
                        StatusDescription = context.Response.StatusDescription,
                        ContentType = context.Response.ContentType,
                        Headers = responseHeaders,
                        Body = responseBody
                    }
                };

                var logMessage = JsonConvert.SerializeObject(logEntry, Newtonsoft.Json.Formatting.Indented);

                // Log as Information level
                _logger.LogInformation(
                    "RequestResponse: {ClassName}.{OperationName} | CorrelationId: {CorrelationId} | ConsumerId: {ConsumerId} | UserId: {UserId} | " +
                    "ExecutionTime: {ExecutionTime}ms | {Method} {Path} | StatusCode: {StatusCode} | " +
                    "TraceId: {TraceId} | SpanId: {SpanId}",
                    className,
                    operationName,
                    correlationId,
                    consumerId,
                    userId,
                    executionTime,
                    context.Request.HttpMethod,
                    context.Request.RawUrl,
                    context.Response.StatusCode,
                    activity?.TraceId.ToString() ?? "N/A",
                    activity?.SpanId.ToString() ?? "N/A");

                // Log detailed JSON for debugging
                _logger.LogDebug("RequestResponse Details: {LogDetails}", logMessage);

                // Also write to Trace
                Trace.TraceInformation(logMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging request/response");
                Trace.TraceError($"Error logging request/response: {ex}");
            }
        }

        /// <summary>
        /// Logs error with request details
        /// </summary>
        private void LogErrorDetails(HttpContext context, Activity? activity, Exception exception, string? requestBody)
        {
            try
            {
                if (!ShouldLog(context))
                    return;

                var startTime = context.Items[RequestStartTimeKey] as DateTime? ?? DateTime.UtcNow;
                var executionTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                var correlationId = context.Items["CorrelationId"] as string ?? "N/A";
                var consumerId = context.Items["ConsumerId"] as string ?? "unknown";
                var userId = context.Items["UserId"] as string ?? "anonymous";
                var className = context.Items["ClassName"] as string ?? "Unknown";
                var operationName = context.Items["OperationName"] as string ?? className;

                // Prepare headers for logging
                var requestHeaders = new Dictionary<string, string>();
                foreach (var key in context.Request.Headers.AllKeys)
                {
                    if (key != null)
                    {
                        requestHeaders[key] = context.Request.Headers[key];
                    }
                }

                // Create structured error log object
                var errorEntry = new
                {
                    LogType = "Error",
                    CorrelationId = correlationId,
                    ConsumerId = consumerId,
                    UserId = userId,
                    ClassName = className,
                    OperationName = operationName,
                    ExecutionTimeMs = executionTime,
                    Request = new
                    {
                        Timestamp = startTime,
                        Method = context.Request.HttpMethod,
                        Path = context.Request.RawUrl,
                        QueryString = context.Request.QueryString.ToString(),
                        ContentType = context.Request.ContentType,
                        Headers = requestHeaders,
                        Body = requestBody
                    },
                    Error = new
                    {
                        Timestamp = DateTime.UtcNow,
                        Message = exception.Message,
                        Type = exception.GetType().Name,
                        StackTrace = exception.StackTrace,
                        InnerException = exception.InnerException?.Message,
                        Source = exception.Source
                    }
                };

                var logMessage = JsonConvert.SerializeObject(errorEntry, Newtonsoft.Json.Formatting.Indented);

                // Log as Error level with structured data
                _logger.LogError(exception,
                    "Error: {ClassName}.{OperationName} | CorrelationId: {CorrelationId} | ConsumerId: {ConsumerId} | UserId: {UserId} | " +
                    "ExecutionTime: {ExecutionTime}ms | {Method} {Path} | ExceptionType: {ExceptionType} | " +
                    "TraceId: {TraceId} | SpanId: {SpanId}",
                    className,
                    operationName,
                    correlationId,
                    consumerId,
                    userId,
                    executionTime,
                    context.Request.HttpMethod,
                    context.Request.RawUrl,
                    exception.GetType().Name,
                    activity?.TraceId.ToString() ?? "N/A",
                    activity?.SpanId.ToString() ?? "N/A");

                // Log detailed JSON for debugging
                _logger.LogDebug("Error Details: {ErrorDetails}", logMessage);

                // Also write to Trace
                Trace.TraceError(logMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging error details");
                Trace.TraceError($"Error logging error details: {ex}");
            }
        }

        /// <summary>
        /// Hook for custom enrichment - override this in a derived class
        /// </summary>
        protected virtual void EnrichActivity(Activity activity, HttpContext context, string eventName)
        {
            // Default implementation - can be overridden
            activity.AddEvent(new ActivityEvent(eventName));
        }

        /// <summary>
        /// Disposes resources used by the module
        /// </summary>
        public void Dispose()
        {
            try
            {
                _activitySource?.Dispose();
                _activitySource = null;
                _logger?.LogInformation("WcfTelemetryHttpModule disposed");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Error disposing WcfTelemetryHttpModule: {ex.Message}");
            }
        }

        private class WcfRequestInfo
        {
            public string? ServiceName { get; set; }
            public string? OperationName { get; set; }
            public string? SoapAction { get; set; }
            public string? Binding { get; set; }
            public string? ContractName { get; set; }
        }

        /// <summary>
        /// Stream filter to capture response content
        /// </summary>
        private class ResponseCaptureFilter : Stream
        {
            private readonly Stream _originalStream;
            private readonly MemoryStream _captureStream = new();
            private readonly HttpContext _context;

            public ResponseCaptureFilter(Stream originalStream, HttpContext context)
            {
                _originalStream = originalStream;
                _context = context;
            }

            public string? GetCapturedContent()
            {
                return _context.Items[ResponseBodyKey] as string;
            }
            private void Store()
            {
                if (_captureStream.Length == 0)
                    return;
                var position = _captureStream.Position;
                _captureStream.Position = 0;
                using (var reader = new StreamReader(_captureStream, Encoding.UTF8, true, 1024, true))
                {
                    var body = reader.ReadToEnd();
                    _context.Items[ResponseBodyKey] = body;
                    _captureStream.Position = position;
                }
            }
            public override void Write(byte[] buffer, int offset, int count)
            {
                _captureStream.Write(buffer, offset, count);
                _originalStream.Write(buffer, offset, count);
            }

            public override void Flush()
            {
                Store();
                _originalStream.Flush();
            }

            public override bool CanRead => _originalStream.CanRead;
            public override bool CanSeek => _originalStream.CanSeek;
            public override bool CanWrite => _originalStream.CanWrite;
            public override long Length => _originalStream.Length;
            public override long Position
            {
                get => _originalStream.Position;
                set => _originalStream.Position = value;
            }

            public override int Read(byte[] buffer, int offset, int count) => _originalStream.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _originalStream.Seek(offset, origin);
            public override void SetLength(long value) => _originalStream.SetLength(value);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _captureStream?.Dispose();
                }
                base.Dispose(disposing);
            }

        }
    }

   
}

#endif