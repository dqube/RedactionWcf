#if NET48_OR_GREATER

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;



namespace mylogging.observability.Framework
{
    /// <summary>
    /// HTTP Module for logging requests and responses with correlation tracking
    /// Handles both WCF services and Web API controllers
    /// Logs request and response as a single combined entry
    /// </summary>
    public class RequestResponseLoggingModule : IHttpModule
    {
        // Static logger instance (initialized once for the entire application)
        private static readonly ILogger _logger;

        // Primary header names
        private const string CorrelationIdHeader = "X-Correlation-Id";
        private const string ConsumerIdHeader = "X-Consumer-Id";
        private const string UserIdHeader = "X-User-Id";

        // Alternative header names to check
        private static readonly string[] CorrelationIdHeaders = new[]
        {
            "X-Correlation-Id",
            "CorrelationId",
            "x-correlation-id",
            "correlationId",
            "Correlation-Id",
            "correlation-id"
        };

        private static readonly string[] ConsumerIdHeaders = new[]
        {
            "X-Consumer-Id",
            "ConsumerId",
            "x-consumer-id",
            "consumerId",
            "Consumer-Id",
            "consumer-id"
        };

        private static readonly string[] UserIdHeaders = new[]
        {
            "X-User-Id",
            "UserId",
            "x-user-id",
            "userId",
            "User-Id",
            "user-id"
        };

        static RequestResponseLoggingModule()
        {
            // Initialize logger factory
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                //builder.AddDebug();
                //builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            _logger = loggerFactory.CreateLogger<RequestResponseLoggingModule>();
        }

        public void Init(HttpApplication context)
        {
            context.BeginRequest += OnBeginRequest;
            context.PreSendRequestHeaders += OnPreSendRequestHeaders;
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

                // Extract correlation information from headers first
                string correlationId = GetHeaderValueWithFallback(request, CorrelationIdHeaders);
                string consumerId = GetHeaderValueWithFallback(request, ConsumerIdHeaders);
                string userId = GetHeaderValueWithFallback(request, UserIdHeaders);

                // Parse ClassName and OperationName from path early
                string className = null;
                string operationName = null;
                ParseClassAndOperationName(request.RawUrl, out className, out operationName);

                // For WCF services (.svc), we need to capture the body BEFORE WCF reads it
                string requestBody = null;
                if (request.RawUrl.Contains(".svc") && request.ContentLength > 0 && request.ContentLength <= 10485760)
                {
                    try
                    {
                        // Read the input stream directly - WCF will buffer it
                        if (request.InputStream.CanSeek)
                        {
                            long originalPosition = request.InputStream.Position;
                            request.InputStream.Position = 0;
                            
                            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8, true, 1024, true))
                            {
                                requestBody = reader.ReadToEnd();
                            }
                            
                            request.InputStream.Position = originalPosition;
                            Debug.WriteLine($"Captured WCF request body: {requestBody?.Length ?? 0} bytes");
                        }
                        else
                        {
                            // For non-seekable streams, we need to use a different approach
                            Debug.WriteLine("Request stream is not seekable - using filter approach");
                            var requestFilter = new RequestCaptureFilter(request.Filter);
                            request.Filter = requestFilter;
                            context.Items["RequestFilter"] = requestFilter;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error capturing request body: {ex.Message}");
                    }
                }
                else if (request.ContentLength > 0 && request.ContentLength <= 10485760)
                {
                    try
                    {
                        // Install request input filter to capture request body BEFORE any stream access
                        var requestFilter = new RequestCaptureFilter(request.Filter);
                        request.Filter = requestFilter;
                        context.Items["RequestFilter"] = requestFilter;
                        Debug.WriteLine("Request filter installed successfully");
                    }
                    catch (Exception filterEx)
                    {
                        Debug.WriteLine($"Failed to install request filter: {filterEx.Message}");
                    }
                }

                // Install response filter to capture response body
                try
                {
                    var responseFilter = new ResponseCaptureFilter(context.Response.Filter);
                    context.Response.Filter = responseFilter;
                    context.Items["ResponseFilter"] = responseFilter;
                    Debug.WriteLine("Response filter installed successfully");
                }
                catch (Exception filterEx)
                {
                    Debug.WriteLine($"Failed to install response filter: {filterEx.Message}");
                }

                // If CorrelationId is still not found, generate a new one
                if (string.IsNullOrEmpty(correlationId))
                {
                    correlationId = Guid.NewGuid().ToString();
                }

                // Store initial context information (body extraction will happen later)
                var requestContext = new RequestResponseContext
                {
                    CorrelationId = correlationId,
                    ConsumerId = consumerId,
                    UserId = userId,
                    RequestTime = DateTime.UtcNow,
                    NeedsBodyExtraction = string.IsNullOrEmpty(consumerId) || string.IsNullOrEmpty(userId),
                    RequestBody = requestBody // Store if we already captured it
                };

                context.Items["RequestContext"] = requestContext;

                // Set initial values in HttpContext.Items for CorrelationContext static access
                context.Items["CorrelationId"] = correlationId;
                if (!string.IsNullOrEmpty(consumerId))
                {
                    context.Items["ConsumerId"] = consumerId;
                }
                if (!string.IsNullOrEmpty(userId))
                {
                    context.Items["UserId"] = userId;
                }
                // Set ClassName and OperationName for CorrelationContext access
                if (!string.IsNullOrEmpty(className))
                {
                    context.Items["ClassName"] = className;
                }
                if (!string.IsNullOrEmpty(operationName))
                {
                    context.Items["OperationName"] = operationName;
                }

                // Add correlation ID to response headers for tracking
                try
                {
                    context.Response.Headers.Add(CorrelationIdHeader, correlationId);
                    if (!string.IsNullOrEmpty(consumerId))
                    {
                        context.Response.Headers.Add(ConsumerIdHeader, consumerId);
                    }
                    if (!string.IsNullOrEmpty(userId))
                    {
                        context.Response.Headers.Add(UserIdHeader, userId);
                    }
                }
                catch (Exception headerEx)
                {
                    Debug.WriteLine($"Failed to add response headers: {headerEx.Message}");
                }

                Debug.WriteLine($"Request filters installed - CorrelationId: {correlationId}, ClassName: {className}, OperationName: {operationName}");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error in RequestResponseLoggingModule.OnBeginRequest: {ex}");
                Debug.WriteLine($"ERROR in OnBeginRequest: {ex}");
            }
        }

        private void OnPreSendRequestHeaders(object sender, EventArgs e)
        {
            var application = (HttpApplication)sender;
            var context = application.Context;

            try
            {
                if (context.Items["RequestContext"] is RequestResponseContext requestContext)
                {
                    // Try to capture response body from Response.OutputStream before it's sent
                    if (context.Request.RawUrl.Contains(".svc"))
                    {
                        try
                        {
                            var responseStream = context.Response.OutputStream;
                            if (responseStream != null && responseStream is MemoryStream ms && ms.CanSeek)
                            {
                                long originalPosition = ms.Position;
                                ms.Position = 0;
                                
                                using (var reader = new StreamReader(ms, Encoding.UTF8, true, 1024, true))
                                {
                                    var responseBody = reader.ReadToEnd();
                                    context.Items["CapturedResponseBody"] = responseBody;
                                    Debug.WriteLine($"Captured WCF response in PreSendRequestHeaders: {responseBody?.Length ?? 0} bytes");
                                }
                                
                                ms.Position = originalPosition;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error capturing response in PreSendRequestHeaders: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in OnPreSendRequestHeaders: {ex}");
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

                    // Get request body - try multiple sources
                    string requestBody = requestContext.RequestBody;
                    
                    // Check WCF inspector first
                    if (string.IsNullOrEmpty(requestBody) && context.Items["WCF_RequestBody"] is string wcfRequestBody)
                    {
                        requestBody = wcfRequestBody;
                        Debug.WriteLine($"[OnEndRequest] Got request body from WCF_RequestBody: {requestBody?.Length ?? 0} bytes");
                    }
                    // Then check Web API handler
                    else if (string.IsNullOrEmpty(requestBody) && context.Items["WebAPI_RequestBody"] is string webApiRequestBody)
                    {
                        requestBody = webApiRequestBody;
                        Debug.WriteLine($"[OnEndRequest] Got request body from WebAPI_RequestBody: {requestBody?.Length ?? 0} bytes");
                    }
                    // Finally check the filter
                    else if (string.IsNullOrEmpty(requestBody) && context.Items["RequestFilter"] is RequestCaptureFilter requestFilter)
                    {
                        requestBody = requestFilter.GetCapturedContent();
                        Debug.WriteLine($"[OnEndRequest] Got request body from RequestFilter: {requestBody?.Length ?? 0} bytes");
                    }

                    // Extract IDs from request body if needed
                    if (requestContext.NeedsBodyExtraction && !string.IsNullOrEmpty(requestBody))
                    {
                        string corrId = requestContext.CorrelationId;
                        string consId = requestContext.ConsumerId;
                        string usrId = requestContext.UserId;
                        
                        ExtractFromRequestBody(requestBody, context.Request.ContentType,
                            ref corrId,
                            ref consId,
                            ref usrId);
                        
                        requestContext.CorrelationId = corrId;
                        requestContext.ConsumerId = consId;
                        requestContext.UserId = usrId;

                        // Update HttpContext.Items with extracted values for CorrelationContext
                        context.Items["CorrelationId"] = corrId;
                        context.Items["ConsumerId"] = consId;
                        context.Items["UserId"] = usrId;
                    }

                    // Parse ClassName and OperationName from path
                    string className = null;
                    string operationName = null;
                    ParseClassAndOperationName(context.Request.RawUrl, out className, out operationName);

                    // Capture request details
                    var requestInfo = new RequestInfo
                    {
                        Timestamp = requestContext.RequestTime,
                        Method = context.Request.HttpMethod,
                        Path = context.Request.RawUrl,
                        ClassName = className,
                        OperationName = operationName,
                        QueryString = context.Request.QueryString.ToString(),
                        ContentType = context.Request.ContentType,
                        Headers = context.Request.Headers,
                        Body = requestBody
                    };

                    requestContext.RequestInfo = requestInfo;

                    // Get response body - try multiple sources
                    string responseBody = null;
                    
                    // First, check if WCF inspector captured it
                    if (context.Items["WCF_ResponseBody"] is string wcfResponseBody)
                    {
                        responseBody = wcfResponseBody;
                        Debug.WriteLine($"[OnEndRequest] Got response from WCF_ResponseBody: {responseBody?.Length ?? 0} bytes");
                    }
                    // Then check if Web API handler captured it
                    else if (context.Items["WebAPI_ResponseBody"] is string webApiResponseBody)
                    {
                        responseBody = webApiResponseBody;
                        Debug.WriteLine($"[OnEndRequest] Got response from WebAPI_ResponseBody: {responseBody?.Length ?? 0} bytes");
                    }
                    // Then check if we captured it in PreSendRequestHeaders
                    else if (context.Items["CapturedResponseBody"] is string capturedResp)
                    {
                        responseBody = capturedResp;
                        Debug.WriteLine($"[OnEndRequest] Got response from CapturedResponseBody: {responseBody?.Length ?? 0} bytes");
                    }
                    // Then check the filter (won't work for WCF but here as fallback)
                    else if (context.Items["ResponseFilter"] is ResponseCaptureFilter responseFilter)
                    {
                        responseBody = responseFilter.GetCapturedContent();
                        Debug.WriteLine($"[OnEndRequest] Got response from ResponseFilter: {responseBody?.Length ?? 0} bytes");
                    }
                    else
                    {
                        Debug.WriteLine($"[OnEndRequest] NO response body found in any location!");
                    }

                    // Capture response details
                    var responseInfo = new ResponseInfo
                    {
                        Timestamp = DateTime.UtcNow,
                        StatusCode = response.StatusCode,
                        StatusDescription = response.StatusDescription,
                        ContentType = response.ContentType,
                        Headers = response.Headers,
                        Body = responseBody
                    };

                    Debug.WriteLine($"Request/Response captured - StatusCode: {response.StatusCode}, Request Body Length: {requestBody?.Length ?? 0}, Response Body Length: {responseBody?.Length ?? 0}");

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

                    // Get request body from filter if not already captured
                    if (requestContext.RequestInfo == null)
                    {
                        string requestBody = null;
                        if (context.Items["RequestFilter"] is RequestCaptureFilter requestFilter)
                        {
                            requestBody = requestFilter.GetCapturedContent();
                        }
                        else if (context.Items["WCF_RequestBody"] is string wcfRequestBody)
                        {
                            requestBody = wcfRequestBody;
                        }

                        requestContext.RequestInfo = new RequestInfo
                        {
                            Timestamp = requestContext.RequestTime,
                            Method = context.Request.HttpMethod,
                            Path = context.Request.RawUrl,
                            QueryString = context.Request.QueryString.ToString(),
                            ContentType = context.Request.ContentType,
                            Headers =   context.Request.Headers,
                            Body = requestBody
                        };
                    }

                    LogError(requestContext, exception, duration);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error in RequestResponseLoggingModule.OnError: {ex}");
                Debug.WriteLine($"ERROR in OnError: {ex}");
            }
        }

        /// <summary>
        /// Extracts ClassName and OperationName from the request path
        /// For WCF: /ServiceName.svc/operation => ClassName=ServiceName, OperationName=operation
        /// For Web API: /api/controller/action => ClassName=controller, OperationName=action
        /// </summary>
        private void ParseClassAndOperationName(string path, out string className, out string operationName)
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

        /// <summary>
        /// Gets header value by checking multiple possible header names.
        /// Returns the first non-empty value found.
        /// </summary>
        private string GetHeaderValueWithFallback(HttpRequest request, string[] headerNames)
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
        private void ExtractFromRequestBody(string body, string contentType, ref string correlationId, ref string consumerId, ref string userId)
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
        private void ExtractFromJson(string body, ref string correlationId, ref string consumerId, ref string userId)
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
        private string GetJsonPropertyValue(JObject jsonObject, params string[] propertyNames)
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
        private void ExtractFromXml(string body, ref string correlationId, ref string consumerId, ref string userId)
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
        private string GetXmlElementValue(XElement parent, params string[] elementNames)
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
        /// Logs combined request and response as a single log entry
        /// </summary>
        private void LogRequestResponse(RequestResponseContext context, ResponseInfo responseInfo, TimeSpan duration)
        {
            var combinedLog = new
            {
                LogType = "RequestResponse",
                context.CorrelationId,
                context.ConsumerId,
                context.UserId,
                context.RequestInfo.ClassName,
                OperationName = context.RequestInfo.OperationName ?? context.RequestInfo.ClassName,
                ExecutionTime = duration.TotalMilliseconds,               
                Request = new
                {
                    context.RequestInfo.Timestamp,
                    context.RequestInfo.Method,
                    context.RequestInfo.Path,
                    context.RequestInfo.QueryString,
                    context.RequestInfo.ContentType,
                    context.RequestInfo.Headers,
                    context.RequestInfo.Body
                },
                Response = new
                {
                    responseInfo.Timestamp,
                    responseInfo.StatusCode,
                    responseInfo.StatusDescription,
                    responseInfo.ContentType,
                    responseInfo.Headers,
                    responseInfo.Body
                }
            };

            string logMessage = JsonConvert.SerializeObject(combinedLog, Formatting.Indented);

            // Log to trace
            Trace.TraceInformation(logMessage);

            // Also log to Debug for development
            _logger.LogInformation(logMessage);
           
        }

        /// <summary>
        /// Logs error with request details
        /// </summary>
        private void LogError(RequestResponseContext context, Exception exception, TimeSpan duration)
        {
            var errorLog = new
            {
                LogType = "Error",
                context.CorrelationId,
                context.ConsumerId,
                context.UserId,
                context.RequestInfo.ClassName,
                OperationName = context.RequestInfo.OperationName ?? context.RequestInfo.ClassName,
                ExecutionTime = duration.TotalMilliseconds,               
                Request = new
                {
                    context.RequestInfo.Timestamp,
                    context.RequestInfo.Method,
                    context.RequestInfo.Path,
                    context.RequestInfo.QueryString,
                    context.RequestInfo.ContentType,
                    context.RequestInfo.Headers,
                    context.RequestInfo.Body
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

            string logMessage = JsonConvert.SerializeObject(errorLog, Formatting.Indented);

            // Log to trace
            Trace.TraceError(logMessage);
            
            // Log with ILogger
            _logger.LogError(exception, "=== ERROR LOG ===");
            _logger.LogError("{LogMessage}", logMessage);
            _logger.LogError("=================");
        }




        public void Dispose()
        {
            // Cleanup if needed
        }

        /// <summary>
        /// Response filter to capture response body
        /// </summary>
        private class ResponseCaptureFilter : Stream
        {
            private readonly Stream _originalStream;
            private readonly MemoryStream _captureStream;

            public ResponseCaptureFilter(Stream originalStream)
            {
                _originalStream = originalStream;
                _captureStream = new MemoryStream();
                Debug.WriteLine($"[ResponseCaptureFilter] Created - OriginalStream type: {originalStream?.GetType().Name}");
            }

            public string GetCapturedContent()
            {
                try
                {
                    if (_captureStream.Length == 0)
                    {
                        Debug.WriteLine($"[ResponseCaptureFilter] GetCapturedContent called but _captureStream is EMPTY");
                        return null;
                    }

                    Debug.WriteLine($"[ResponseCaptureFilter] GetCapturedContent - Stream length: {_captureStream.Length} bytes");
                    _captureStream.Position = 0;
                    using (var reader = new StreamReader(_captureStream, Encoding.UTF8, true, 1024, true))
                    {
                        var content = reader.ReadToEnd();
                        Debug.WriteLine($"[ResponseCaptureFilter] Successfully read {content?.Length ?? 0} characters");
                        return content;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ResponseCaptureFilter] ERROR in GetCapturedContent: {ex.Message}");
                    return null;
                }
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                Debug.WriteLine($"[ResponseCaptureFilter] Write called - {count} bytes");
                _captureStream.Write(buffer, offset, count);
                _originalStream.Write(buffer, offset, count);
            }

            public override void Flush()
            {
                Debug.WriteLine($"[ResponseCaptureFilter] Flush called");
                _originalStream.Flush();
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => _originalStream.Length;

            public override long Position
            {
                get => _originalStream.Position;
                set => _originalStream.Position = value;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return _originalStream.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                _originalStream.SetLength(value);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Debug.WriteLine($"[ResponseCaptureFilter] Disposing - Captured {_captureStream.Length} bytes total");
                    _captureStream?.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Request filter to capture request body for non-seekable streams
        /// </summary>
        private class RequestCaptureFilter : Stream
        {
            private readonly Stream _originalStream;
            private readonly MemoryStream _captureStream;
            private readonly bool _canRead;

            public RequestCaptureFilter(Stream originalStream)
            {
                // Handle case where originalStream might be null (first filter in chain)
                _originalStream = originalStream ?? new MemoryStream();
                _captureStream = new MemoryStream();
                _canRead = _originalStream.CanRead;
            }

            public string GetCapturedContent()
            {
                try
                {
                    if (_captureStream.Length == 0)
                        return null;

                    _captureStream.Position = 0;
                    using (var reader = new StreamReader(_captureStream, Encoding.UTF8, true, 1024, true))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch
                {
                    return null;
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int bytesRead = _originalStream.Read(buffer, offset, count);
                if (bytesRead > 0)
                {
                    _captureStream.Write(buffer, offset, bytesRead);
                }
                return bytesRead;
            }

            public override void Flush()
            {
                _originalStream.Flush();
            }

            public override bool CanRead => _canRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            
            public override long Length
            {
                get
                {
                    try
                    {
                        return _originalStream.Length;
                    }
                    catch
                    {
                        return 0;
                    }
                }
            }

            public override long Position
            {
                get
                {
                    try
                    {
                        return _originalStream.Position;
                    }
                    catch
                    {
                        return 0;
                    }
                }
                set
                {
                    try
                    {
                        _originalStream.Position = value;
                    }
                    catch
                    {
                        // Ignore if stream doesn't support position
                    }
                }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _captureStream?.Dispose();
                }
                base.Dispose(disposing);
            }
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
            public bool NeedsBodyExtraction { get; set; }
            public string RequestBody { get; set; } // Added to store pre-captured body
        }

        /// <summary>
        /// Request details
        /// </summary>
        private class RequestInfo
        {
            public DateTime Timestamp { get; set; }
            public string Method { get; set; }
            public string Path { get; set; }
            public string ClassName { get; set; }
            public string OperationName { get; set; }
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
        /// </summary>
        private bool ShouldSkipLogging(HttpRequest request)
        {
            var path = request.RawUrl.ToLowerInvariant();
            var contentType = request.ContentType?.ToLowerInvariant() ?? string.Empty;
            var acceptHeader = request.Headers["Accept"]?.ToLowerInvariant() ?? string.Empty;

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
                "/home", "/home/index", "/help", "/areas/helppage", "/content/",
                "/scripts/", "/bundles/", "/fonts/", "/images/", "/favicon.ico",
                "/__browserlink", "/trace.axd", "/glimpse.axd"
            };

            if (excludedPaths.Any(excluded => path.StartsWith(excluded) || path.Contains(excluded)))
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
    }
}
#endif