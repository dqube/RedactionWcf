using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RedactionCore.Middleware
{
    /// <summary>
    /// Middleware for logging requests and responses with correlation tracking
    /// Handles API controllers and logs request and response as a single combined entry
    /// </summary>
    public class RequestResponseLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestResponseLoggingMiddleware> _logger;

        private const string CorrelationIdHeader = "X-Correlation-Id";
        private const string ConsumerIdHeader = "X-Consumer-Id";
        private const string UserIdHeader = "X-User-Id";

        private static readonly string[] CorrelationIdHeaders = new[]
        {
                "X-Correlation-Id", "CorrelationId", "x-correlation-id",
                "correlationId", "Correlation-Id", "correlation-id"
            };

        private static readonly string[] ConsumerIdHeaders = new[]
        {
                "X-Consumer-Id", "ConsumerId", "x-consumer-id",
                "consumerId", "Consumer-Id", "consumer-id"
            };

        private static readonly string[] UserIdHeaders = new[]
        {
                "X-User-Id", "UserId", "x-user-id",
                "userId", "User-Id", "user-id"
            };

        public RequestResponseLoggingMiddleware(RequestDelegate next, ILogger<RequestResponseLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

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

            // Extract correlation information from headers
            string? correlationId = GetHeaderValueWithFallback(context.Request.Headers, CorrelationIdHeaders);
            string? consumerId = GetHeaderValueWithFallback(context.Request.Headers, ConsumerIdHeaders);
            string? userId = GetHeaderValueWithFallback(context.Request.Headers, UserIdHeaders);

            // Generate CorrelationId if not provided
            if (string.IsNullOrEmpty(correlationId))
            {
                correlationId = Guid.NewGuid().ToString();
            }

            // Capture request body
            string? requestBody = null;
            if (context.Request.ContentLength > 0 && context.Request.ContentLength <= 10485760) // 10MB limit
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

                // Log combined request/response
                LogRequestResponse(correlationId, consumerId, userId, requestInfo, responseInfo, stopwatch.Elapsed);

                // Copy response body back to original stream
                responseBodyStream.Seek(0, SeekOrigin.Begin);
                await responseBodyStream.CopyToAsync(originalBodyStream);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Log error
                LogError(correlationId, consumerId, userId, requestInfo, ex, stopwatch.Elapsed);

                throw;
            }
            finally
            {
                context.Response.Body = originalBodyStream;
            }
        }

        /// <summary>
        /// Reads the request body and resets the stream position
        /// </summary>
        private async Task<string?> ReadRequestBodyAsync(HttpRequest request)
        {
            try
            {
                request.EnableBuffering();

                using var reader = new StreamReader(
                    request.Body,
                    encoding: Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 1024,
                    leaveOpen: true);

                var body = await reader.ReadToEndAsync();
                request.Body.Position = 0;

                return body;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading request body: {ex.Message}");
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
            RequestInfo requestInfo, ResponseInfo responseInfo, TimeSpan duration)
        {
            var combinedLog = new
            {
                LogType = "RequestResponse",
                CorrelationId = correlationId,
                ConsumerId = consumerId,
                UserId = userId,
                ClassName = requestInfo.ClassName,
                OperationName = requestInfo.OperationName ?? requestInfo.ClassName,
                ExecutionTime = duration.TotalMilliseconds,
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

            _logger.LogInformation("Request/Response loging for {ClassName}.{props}",
                requestInfo.ClassName,
               combinedLog);

            Debug.WriteLine("=== REQUEST/RESPONSE LOG ===");
            Debug.WriteLine(logMessage);
            Debug.WriteLine("============================");
        }

        /// <summary>
        /// Logs error with request details
        /// </summary>
        private void LogError(string correlationId, string? consumerId, string? userId,
            RequestInfo requestInfo, Exception exception, TimeSpan duration)
        {
            var errorLog = new
            {
                LogType = "Error",
                CorrelationId = correlationId,
                ConsumerId = consumerId,
                UserId = userId,
                ClassName = requestInfo.ClassName,
                OperationName = requestInfo.OperationName ?? requestInfo.ClassName,
                ExecutionTime = duration.TotalMilliseconds,
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
                    Message = exception.Message,
                    Type = exception.GetType().Name,
                    StackTrace = exception.StackTrace,
                    InnerException = exception.InnerException?.Message
                }
            };

            var logMessage = JsonSerializer.Serialize(errorLog, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            _logger.LogError(exception, "Error processing request for {ClassName}.{obj}",
                requestInfo.ClassName,
                errorLog);

         
        }

        /// <summary>
        /// Determines if the request should be excluded from logging
        /// </summary>
        private bool ShouldSkipLogging(HttpRequest request)
        {
            var path = request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            var contentType = request.ContentType?.ToLowerInvariant() ?? string.Empty;
            var acceptHeader = request.Headers["Accept"].ToString().ToLowerInvariant();

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

            // Skip common paths
            var excludedPaths = new[]
            {
                    "/swagger", "/health", "/metrics", "/favicon.ico", "/_framework", "/_content"
                };

            if (excludedPaths.Any(excluded => path.StartsWith(excluded)))
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
