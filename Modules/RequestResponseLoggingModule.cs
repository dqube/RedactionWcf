using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Diagnostics;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ServiceModel.Channels;

namespace RedactionWcf.Modules
{
    /// <summary>
    /// HTTP Module for logging requests and responses with correlation tracking
    /// Handles both WCF services and Web API controllers
    /// Logs request and response as a single combined entry
    /// </summary>
    public class RequestResponseLoggingModule : IHttpModule
    {
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
                    Debug.WriteLine($"Skipping logging for: {request.RawUrl}");
                    return;
                }

                Debug.WriteLine($"Processing logging for: {request.RawUrl}");

                // Extract correlation information from headers first
                string correlationId = GetHeaderValueWithFallback(request, CorrelationIdHeaders);
                string consumerId = GetHeaderValueWithFallback(request, ConsumerIdHeaders);
                string userId = GetHeaderValueWithFallback(request, UserIdHeaders);

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

                Debug.WriteLine($"Request filters installed - CorrelationId: {correlationId}");
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

                    // Get request body - either from context (WCF) or from filter
                    string requestBody = requestContext.RequestBody;
                    
                    if (string.IsNullOrEmpty(requestBody) && context.Items["RequestFilter"] is RequestCaptureFilter requestFilter)
                    {
                        requestBody = requestFilter.GetCapturedContent();
                    }
                    else if (string.IsNullOrEmpty(requestBody) && context.Items["WCF_RequestBody"] is string wcfRequestBody)
                    {
                        requestBody = wcfRequestBody;
                        Debug.WriteLine($"[OnEndRequest] Got request body from WCF_RequestBody: {requestBody?.Length ?? 0} bytes");
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
                    }

                    // Capture request details
                    var requestInfo = new RequestInfo
                    {
                        Timestamp = requestContext.RequestTime,
                        Method = context.Request.HttpMethod,
                        Path = context.Request.RawUrl,
                        QueryString = context.Request.QueryString.ToString(),
                        ContentType = context.Request.ContentType,
                        Headers = GetSafeHeaders(context.Request.Headers),
                        Body = SanitizeBody(requestBody)
                    };

                    requestContext.RequestInfo = requestInfo;

                    // Get response body - try multiple sources with detailed logging
                    string responseBody = null;
                    
                    Debug.WriteLine($"[OnEndRequest] Checking for response body in context.Items...");
                    Debug.WriteLine($"[OnEndRequest] CapturedResponseBody exists: {context.Items.Contains("CapturedResponseBody")}");
                    Debug.WriteLine($"[OnEndRequest] ResponseFilter exists: {context.Items.Contains("ResponseFilter")}");
                    Debug.WriteLine($"[OnEndRequest] WCF_ResponseBody exists: {context.Items.Contains("WCF_ResponseBody")}");
                    
                    // First, check if WCF inspector captured it
                    if (context.Items["WCF_ResponseBody"] is string wcfResponseBody)
                    {
                        responseBody = wcfResponseBody;
                        Debug.WriteLine($"[OnEndRequest] Got response from WCF_ResponseBody: {responseBody?.Length ?? 0} bytes");
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
                        Headers = GetSafeHeaders(response.Headers),
                        Body = SanitizeBody(responseBody)
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
                            Headers = GetSafeHeaders(context.Request.Headers),
                            Body = SanitizeBody(requestBody)
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
            }

            public string GetCapturedContent()
            {
                try
                {
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

            public override void Write(byte[] buffer, int offset, int count)
            {
                _captureStream.Write(buffer, offset, count);
                _originalStream.Write(buffer, offset, count);
            }

            public override void Flush()
            {
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
