#if NET8_0
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace mylogging.observability
{
    /// <summary>
    /// Middleware for logging requests and responses with correlation tracking
    /// Handles API controllers and logs request and response as a single combined entry
    /// Includes OpenTelemetry distributed tracing support
    /// </summary>
    public class RequestResponseLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestResponseLoggingMiddleware> _logger;
        private readonly ObservabilityOptions _options;
        private static ActivitySource? _activitySource;
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

        private const string CorrelationIdHeader = "X-Correlation-Id";
        private const string ConsumerIdHeader = "X-Consumer-Id";
        private const string UserIdHeader = "X-User-Id";
        private const string ActivityKey = "RequestResponseLogging.Activity";
        private const string RequestStartTimeKey = "RequestResponseLogging.RequestStartTime";

        private static ActivitySource ActivitySource
        {
            get
            {
                if (_activitySource == null)
                {
                    _activitySource = new ActivitySource("RequestResponseLogging", "1.0.0");
                }
                return _activitySource;
            }
        }

        // Get header name arrays from options or use defaults
        private string[] CorrelationIdHeaders => _options?.RequestResponseLogging?.CorrelationIdHeaders?.ToArray() ?? new[] {
            "X-Correlation-Id", "CorrelationId", "x-correlation-id",
            "correlationId", "Correlation-Id", "correlation-id"
        };

        private string[] ConsumerIdHeaders => _options?.RequestResponseLogging?.ConsumerIdHeaders?.ToArray() ?? new[] {
            "X-Consumer-Id", "ConsumerId", "x-consumer-id",
            "consumerId", "Consumer-Id", "consumer-id"
        };

        private string[] UserIdHeaders => _options?.RequestResponseLogging?.UserIdHeaders?.ToArray() ?? new[] {
            "X-User-Id", "UserId", "x-user-id",
            "userId", "User-Id", "user-id"
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="RequestResponseLoggingMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next middleware in the pipeline.</param>
        /// <param name="logger">The logger instance for this middleware.</param>
        /// <param name="options">The observability configuration options.</param>
        public RequestResponseLoggingMiddleware(RequestDelegate next, ILogger<RequestResponseLoggingMiddleware> logger,
             IOptions<ObservabilityOptions> options)
        {
            _next = next;
            _logger = logger;
            _options = options.Value;

            // Initialize ActivitySource with service name from options
            if (_activitySource == null && _options != null)
            {
                _activitySource = new ActivitySource(_options.ServiceName ?? "RequestResponseLogging",
                    _options.ServiceVersion ?? "1.0.0");
            }
        }

        /// <summary>
        /// Invokes the middleware to log HTTP request and response information.
        /// </summary>
        /// <param name="context">The HTTP context for the current request.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            // Skip logging for static resources and non-API requests
            if (ShouldSkipLogging(context.Request))
            {
                await _next(context);
                return;
            }

            var startTime = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            context.Items[RequestStartTimeKey] = startTime;

            // Extract trace context from incoming request headers using Propagator
            var parentContext = Propagator.Extract(default, context.Request.Headers, ExtractTraceContext);
            Baggage.Current = parentContext.Baggage;

            // Extract correlation information from headers
            string? correlationId = GetHeaderValueWithFallback(context.Request.Headers, CorrelationIdHeaders);
            string? consumerId = GetHeaderValueWithFallback(context.Request.Headers, ConsumerIdHeaders);
            string? userId = GetHeaderValueWithFallback(context.Request.Headers, UserIdHeaders);

            // Generate CorrelationId if not provided
            if (string.IsNullOrEmpty(correlationId))
            {
                correlationId = Guid.NewGuid().ToString();
            }

            // Enable request body buffering by copying to a seekable stream
            var originalRequestBody = context.Request.Body;
            var buffer = new MemoryStream();
            // This copies the entire request body into memory
            await context.Request.Body.CopyToAsync(buffer);
            buffer.Position = 0;
            context.Request.Body = buffer;

            // Capture request body
            string? requestBody = null;
            if (buffer.Length > 0 && buffer.Length <= 10485760) // 10MB limit
            {
                requestBody = await ReadRequestBodyAsync(context.Request);
            }

            // Extract IDs from body if not in headers
            bool needsBodyExtraction = string.IsNullOrEmpty(consumerId) || string.IsNullOrEmpty(userId);
            if (needsBodyExtraction && !string.IsNullOrEmpty(requestBody))
            {
                ExtractFromRequestBody(requestBody, context.Request.ContentType ?? string.Empty,
                    ref correlationId, ref consumerId, ref userId);
            }

            // Parse ClassName and OperationName from path
            ParseClassAndOperationName(context.Request.Path, out string? className, out string? operationName);
            if (string.IsNullOrEmpty(operationName))
            {
                operationName = className;
            }

            // Store correlation info in HttpContext.Items for downstream access
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

            // Start activity with extracted parent context
            Activity? activity = null;
            activity = ActivitySource.StartActivity(
                $"{className}.{operationName}",
                ActivityKind.Server,
                parentContext.ActivityContext);

            // Fallback mechanism if ActivitySource returns null
            if (activity == null)
            {
                _logger.LogWarning($"ActivitySource.StartActivity returned null. ActivitySource name: {ActivitySource.Name}. " +
                                 "Ensure OpenTelemetry TracerProvider is configured with AddSource(\"{ActivitySourceName}\")",
                                 ActivitySource.Name);

                // Try to use existing Activity.Current or create a new one manually
                if (Activity.Current != null)
                {
                    activity = Activity.Current;
                }
                else
                {
                    // Manual activity creation as last resort
                    activity = new Activity($"{className}.{operationName}");
                    activity.SetParentId(parentContext.ActivityContext.TraceId,
                        parentContext.ActivityContext.SpanId,
                        parentContext.ActivityContext.TraceFlags);
                    activity.Start();
                }
            }

            if (activity != null)
            {
                // Set Activity.Current so it can be accessed in downstream code
                Activity.Current = activity;

                // Set standard OpenTelemetry semantic conventions
                activity.SetTag("http.method", context.Request.Method);
                activity.SetTag("http.url", $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}");
                activity.SetTag("http.target", context.Request.Path + context.Request.QueryString.ToString());
                activity.SetTag("http.host", context.Request.Host.ToString());
                activity.SetTag("http.scheme", context.Request.Scheme);
                activity.SetTag("net.host.name", context.Request.Host.Host);
                if (context.Request.Host.Port.HasValue)
                {
                    activity.SetTag("net.host.port", context.Request.Host.Port.Value);
                }

                // Add service information
                if (!string.IsNullOrEmpty(_options?.ServiceName))
                    activity.SetTag("service.name", _options.ServiceName);
                if (!string.IsNullOrEmpty(_options?.ServiceVersion))
                    activity.SetTag("service.version", _options.ServiceVersion);

                // Add correlation IDs to activity
                if (!string.IsNullOrEmpty(correlationId))
                    activity.SetTag("correlation.id", correlationId);
                if (!string.IsNullOrEmpty(consumerId))
                    activity.SetTag("consumer.id", consumerId);
                if (!string.IsNullOrEmpty(userId))
                    activity.SetTag("user.id", userId);

                // Add class and operation name
                if (!string.IsNullOrEmpty(className))
                    activity.SetTag("code.namespace", className);
                if (!string.IsNullOrEmpty(operationName))
                    activity.SetTag("code.function", operationName);

                // Add user agent and client IP
                if (context.Request.Headers.TryGetValue("User-Agent", out var userAgent))
                    activity.SetTag("http.user_agent", userAgent.ToString());

                var clientIp = context.Connection.RemoteIpAddress?.ToString();
                if (!string.IsNullOrEmpty(clientIp))
                    activity.SetTag("net.peer.ip", clientIp);

                // Log request body size
                if (requestBody != null)
                    activity.SetTag("http.request_content_length", requestBody.Length);

                // Store activity in context for later retrieval
                context.Items[ActivityKey] = activity;

                // Add enrichment event
                activity.AddEvent(new ActivityEvent("RequestStarted"));
            }

            // Capture request details
            var requestInfo = new RequestInfo
            {
                Timestamp = startTime,
                Method = context.Request.Method,
                Path = context.Request.Path,
                ClassName = className,
                OperationName = operationName,
                QueryString = context.Request.QueryString.ToString(),
                ContentType = context.Request.ContentType,
                Headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
                Body = requestBody
            };

            // Add correlation headers to response
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            if (!string.IsNullOrEmpty(consumerId))
            {
                context.Response.Headers[ConsumerIdHeader] = consumerId;
            }
            if (!string.IsNullOrEmpty(userId))
            {
                context.Response.Headers[UserIdHeader] = userId;
            }

            // Replace response body stream to capture response
            var originalBodyStream = context.Response.Body;
            using var responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;

            try
            {
                // Call the next middleware
                await _next(context);

                stopwatch.Stop();

                // Capture response body
                string? responseBody = await ReadResponseBodyAsync(context.Response);

                // Capture response details
                var responseInfo = new ResponseInfo
                {
                    Timestamp = DateTime.UtcNow,
                    StatusCode = context.Response.StatusCode,
                    ContentType = context.Response.ContentType,
                    Headers = context.Response.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
                    Body = responseBody
                };

                // Update activity with response information
                if (activity != null)
                {
                    activity.SetTag("http.status_code", context.Response.StatusCode);

                    if (responseBody != null)
                        activity.SetTag("http.response_content_length", responseBody.Length);

                    // Set activity status based on response
                    if (context.Response.StatusCode >= 400)
                        activity.SetStatus(ActivityStatusCode.Error, $"HTTP {context.Response.StatusCode}");
                    else
                        activity.SetStatus(ActivityStatusCode.Ok);

                    activity.AddEvent(new ActivityEvent("RequestCompleted"));
                }

                // Log combined request/response
                LogRequestResponse(correlationId, consumerId, userId, requestInfo, responseInfo,
                    stopwatch.Elapsed, activity);

                // Copy response body back to original stream
                responseBodyStream.Seek(0, SeekOrigin.Begin);
                await responseBodyStream.CopyToAsync(originalBodyStream);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Update activity with error information
                if (activity != null)
                {
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    // activity.RecordException(ex);  // Remove this obsolete line
                    activity.SetTag("error", true);
                    activity.SetTag("exception.type", ex.GetType().FullName);
                    activity.SetTag("exception.message", ex.Message);
                    if (ex.StackTrace != null)
                        activity.SetTag("exception.stacktrace", ex.StackTrace);
                }

                // Log error
                LogError(correlationId, consumerId, userId, requestInfo, ex, stopwatch.Elapsed, activity);

                throw;
            }
            finally
            {
                context.Response.Body = originalBodyStream;

                // Dispose activity properly
                if (activity != null)
                {
                    try
                    {
                        // Stop the activity (marks end time and duration)
                        activity.Stop();

                        // Dispose the activity (releases resources)
                        activity.Dispose();

                        // Clear the activity from context to prevent double-disposal
                        context.Items.Remove(ActivityKey);

                        // Reset Activity.Current if it's our activity to prevent memory leaks
                        if (Activity.Current == activity)
                            Activity.Current = null;
                    }
                    catch (Exception disposeEx)
                    {
                        // Log but don't throw - disposal errors shouldn't break the pipeline
                        _logger.LogWarning(disposeEx, "Error disposing activity");
                    }
                }
            }
        }

        /// <summary>
        /// Extracts trace context from HTTP headers for distributed tracing
        /// </summary>
        private IEnumerable<string> ExtractTraceContext(IHeaderDictionary headers, string key)
        {
            if (headers.TryGetValue(key, out var values))
            {
                return values.ToArray();
            }
            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// Reads the request body and resets the stream position
        /// </summary>
        private async Task<string?> ReadRequestBodyAsync(HttpRequest request)
        {
            try
            {
                // Ensure we're at the beginning of the stream
                request.Body.Position = 0;

                using var reader = new StreamReader(
                    request.Body,
                    encoding: Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 1024,
                    leaveOpen: true);

                var body = await reader.ReadToEndAsync();

                // Reset position for the next middleware to read
                request.Body.Position = 0;

                Debug.WriteLine($"Successfully read request body: {body?.Length ?? 0} characters");
                return body;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading request body: {ex.Message}");
                _logger.LogWarning(ex, "Failed to read request body");
                return null;
            }
        }

        /// <summary>
        /// Reads the response body from the memory stream
        /// </summary>
        private async Task<string?> ReadResponseBodyAsync(HttpResponse response)
        {
            try
            {
                response.Body.Seek(0, SeekOrigin.Begin);

                using var reader = new StreamReader(
                    response.Body,
                    encoding: Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 1024,
                    leaveOpen: true);

                var body = await reader.ReadToEndAsync();
                response.Body.Seek(0, SeekOrigin.Begin);

                return body;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading response body: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts ClassName and OperationName from the request path
        /// For Web API: /api/controller/action => ClassName=controller, OperationName=action
        /// </summary>
        private void ParseClassAndOperationName(PathString path, out string? className, out string? operationName)
        {
            className = null;
            operationName = null;

            try
            {
                var pathValue = path.Value;
                if (string.IsNullOrEmpty(pathValue))
                    return;

                // Split path into segments
                var segments = pathValue.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                if (segments.Length == 0)
                    return;

                // Handle Web API path: /api/controller/action
                if (pathValue.ToLowerInvariant().StartsWith("/api/"))
                {
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
                else if (segments.Length == 1)
                {
                    className = segments[0];
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing class and operation name from path '{path}': {ex.Message}");
            }
        }

        /// <summary>
        /// Gets header value by checking multiple possible header names
        /// </summary>
        private string? GetHeaderValueWithFallback(IHeaderDictionary headers, string[] headerNames)
        {
            foreach (var headerName in headerNames)
            {
                if (headers.TryGetValue(headerName, out var value) && !string.IsNullOrEmpty(value))
                {
                    return value.ToString();
                }
            }
            return null;
        }

        /// <summary>
        /// Extracts IDs from request body
        /// </summary>
        private void ExtractFromRequestBody(string body, string contentType,
            ref string correlationId, ref string consumerId, ref string userId)
        {
            try
            {
                if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(contentType))
                    return;

                contentType = contentType.ToLowerInvariant();

                // Handle JSON content
                if (contentType.Contains("application/json"))
                {
                    ExtractFromJson(body, ref correlationId, ref consumerId, ref userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to extract IDs from request body: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract correlation IDs from JSON request body
        /// </summary>
        private void ExtractFromJson(string body, ref string correlationId, ref string consumerId, ref string userId)
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(body);
                var root = jsonDoc.RootElement;

                // Extract CorrelationId
                if (string.IsNullOrEmpty(correlationId))
                {
                    correlationId = GetJsonPropertyValue(root, CorrelationIdHeaders) ?? correlationId;

                    // Check nested header/headers object
                    if (string.IsNullOrEmpty(correlationId))
                    {
                        if (root.TryGetProperty("header", out var header) ||
                            root.TryGetProperty("headers", out header) ||
                            root.TryGetProperty("Header", out header) ||
                            root.TryGetProperty("Headers", out header))
                        {
                            correlationId = GetJsonPropertyValue(header, CorrelationIdHeaders) ?? correlationId;
                        }
                    }
                }

                // Extract ConsumerId
                if (string.IsNullOrEmpty(consumerId))
                {
                    consumerId = GetJsonPropertyValue(root, ConsumerIdHeaders) ?? consumerId;

                    if (string.IsNullOrEmpty(consumerId))
                    {
                        if (root.TryGetProperty("header", out var header) ||
                            root.TryGetProperty("headers", out header) ||
                            root.TryGetProperty("Header", out header) ||
                            root.TryGetProperty("Headers", out header))
                        {
                            consumerId = GetJsonPropertyValue(header, ConsumerIdHeaders) ?? consumerId;
                        }
                    }
                }

                // Extract UserId
                if (string.IsNullOrEmpty(userId))
                {
                    userId = GetJsonPropertyValue(root, UserIdHeaders) ?? userId;

                    if (string.IsNullOrEmpty(userId))
                    {
                        if (root.TryGetProperty("header", out var header) ||
                            root.TryGetProperty("headers", out header) ||
                            root.TryGetProperty("Header", out header) ||
                            root.TryGetProperty("Headers", out header))
                        {
                            userId = GetJsonPropertyValue(header, UserIdHeaders) ?? userId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to parse JSON body: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to get JSON property value by checking multiple property names
        /// </summary>
        private string? GetJsonPropertyValue(JsonElement element, params string[] propertyNames)
        {
            foreach (var propName in propertyNames)
            {
                if (element.TryGetProperty(propName, out var value))
                {
                    var strValue = value.ToString();
                    if (!string.IsNullOrEmpty(strValue))
                    {
                        return strValue;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Logs combined request and response as a single log entry
        /// </summary>
        private void LogRequestResponse(string correlationId, string? consumerId, string? userId,
            RequestInfo requestInfo, ResponseInfo responseInfo, TimeSpan duration, Activity? activity = null)
        {
            // Check if logging should occur
            if (_options?.EnableRequestResponseLogging == false)
                return;

            // For detailed debugging, still use JSON serialization in Debug output
            var combinedLog = new
            {
                LogType = "RequestResponse",
                CorrelationId = correlationId,
                ConsumerId = consumerId,
                UserId = userId,
                requestInfo.ClassName,
                OperationName = requestInfo.OperationName ?? requestInfo.ClassName,
                ExecutionTime = duration.TotalMilliseconds,
                TraceId = activity?.TraceId.ToString() ?? "N/A",
                SpanId = activity?.SpanId.ToString() ?? "N/A",
                Request = new
                {
                    requestInfo.Timestamp,
                    requestInfo.Method,
                    requestInfo.Path,
                    requestInfo.QueryString,
                    requestInfo.ContentType,
                    requestInfo.Headers,
                    requestInfo.Body
                },
                Response = new
                {
                    responseInfo.Timestamp,
                    responseInfo.StatusCode,
                    responseInfo.ContentType,
                    responseInfo.Headers,
                    responseInfo.Body
                }
            };

            var logMessage = JsonSerializer.Serialize(combinedLog, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            _logger.LogInformation(
               "Request/Response: {ClassName}.{OperationName} | CorrelationId: {CorrelationId} | ConsumerId: {ConsumerId} | UserId: {UserId} | " +
               "ExecutionTime: {ExecutionTime}ms | StatusCode: {StatusCode} | " +
               "{Method} {Path} | TraceId: {TraceId} | SpanId: {SpanId} | " +
               "RequestHeaders: {@RequestHeaders} | ResponseHeaders: {@ResponseHeaders} | " +
               "RequestBody: {RequestBody} | ResponseBody: {ResponseBody}",
               requestInfo.ClassName,
               requestInfo.OperationName ?? requestInfo.ClassName,
               correlationId,
               consumerId ?? "unknown",
               userId ?? "anonymous",
               duration.TotalMilliseconds,
               responseInfo.StatusCode,
               requestInfo.Method,
               requestInfo.Path,
               activity?.TraceId.ToString() ?? "N/A",
               activity?.SpanId.ToString() ?? "N/A",
               requestInfo.Headers,
               responseInfo.Headers,
               requestInfo.Body,
               responseInfo.Body);

            Debug.WriteLine("=== REQUEST/RESPONSE LOG ===");
            Debug.WriteLine(logMessage);
            Debug.WriteLine("============================");
        }

        /// <summary>
        /// Logs error with request details
        /// </summary>
        private void LogError(string correlationId, string? consumerId, string? userId,
            RequestInfo requestInfo, Exception exception, TimeSpan duration, Activity? activity = null)
        {
            var errorLog = new
            {
                LogType = "Error",
                CorrelationId = correlationId,
                ConsumerId = consumerId,
                UserId = userId,
                requestInfo.ClassName,
                OperationName = requestInfo.OperationName ?? requestInfo.ClassName,
                ExecutionTime = duration.TotalMilliseconds,
                TraceId = activity?.TraceId.ToString() ?? "N/A",
                SpanId = activity?.SpanId.ToString() ?? "N/A",
                Request = new
                {
                    requestInfo.Timestamp,
                    requestInfo.Method,
                    requestInfo.Path,
                    requestInfo.QueryString,
                    requestInfo.ContentType,
                    requestInfo.Headers,
                    requestInfo.Body
                },
                Error = new
                {
                    Timestamp = DateTime.UtcNow,
                    exception.Message,
                    Type = exception.GetType().Name,
                    exception.StackTrace,
                    InnerException = exception.InnerException?.Message
                }
            };

            var logMessage = JsonSerializer.Serialize(errorLog, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            _logger.LogError(exception,
                "Error: {ClassName}.{OperationName} | CorrelationId: {CorrelationId} | ConsumerId: {ConsumerId} | UserId: {UserId} | " +
                "ExecutionTime: {ExecutionTime}ms | {Method} {Path} | " +
                "TraceId: {TraceId} | SpanId: {SpanId} | " +
                "RequestHeaders: {@RequestHeaders} | RequestBody: {RequestBody} | " +
                "ExceptionType: {ExceptionType} | ExceptionMessage: {ExceptionMessage}",
                requestInfo.ClassName,
                requestInfo.OperationName ?? requestInfo.ClassName,
                correlationId,
                consumerId ?? "unknown",
                userId ?? "anonymous",
                duration.TotalMilliseconds,
                requestInfo.Method,
                requestInfo.Path,
                activity?.TraceId.ToString() ?? "N/A",
                activity?.SpanId.ToString() ?? "N/A",
                requestInfo.Headers,
                requestInfo.Body,
                exception.GetType().Name,
                exception.Message);

            Debug.WriteLine("=== ERROR LOG ===");
            Debug.WriteLine(logMessage);
            Debug.WriteLine("=================");
        }

        /// <summary>
        /// Determines if the request should be excluded from logging
        /// </summary>
        private bool ShouldSkipLogging(HttpRequest request)
        {
            var path = request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            var contentType = request.ContentType?.ToLowerInvariant() ?? string.Empty;
            var acceptHeader = request.Headers["Accept"].ToString().ToLowerInvariant();

            // Check excluded paths from configuration
            if (_options?.RequestResponseLogging?.ExcludePaths != null && _options.RequestResponseLogging.ExcludePaths.Any())
            {
                if (_options.RequestResponseLogging.ExcludePaths.Any(excluded =>
                    path.Contains(excluded.ToLowerInvariant())))
                {
                    return true;
                }
            }
            else
            {
                // Use default excluded paths
                var excludedPaths = new[] {
                    "/swagger", "/health", "/metrics", "/favicon.ico", "/_framework", "/_content"
                };

                if (excludedPaths.Any(excluded => path.StartsWith(excluded)))
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
            bool isApiRequest = path.StartsWith("/api/") ||
                               acceptHeader.Contains("application/json") ||
                               contentType.Contains("application/json");

            return !isApiRequest;
        }

        /// <summary>
        /// Request details
        /// </summary>
        private class RequestInfo
        {
            public DateTime Timestamp { get; set; }
            public required string Method { get; set; }
            public required string Path { get; set; }
            public string? ClassName { get; set; }
            public string? OperationName { get; set; }
            public required string QueryString { get; set; }
            public string? ContentType { get; set; }
            public required Dictionary<string, string> Headers { get; set; }
            public string? Body { get; set; }
        }

        /// <summary>
        /// Response details
        /// </summary>
        private class ResponseInfo
        {
            public DateTime Timestamp { get; set; }
            public int StatusCode { get; set; }
            public string? ContentType { get; set; }
            public required Dictionary<string, string> Headers { get; set; }
            public string? Body { get; set; }
        }
    }
}
#endif