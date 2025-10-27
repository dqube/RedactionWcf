#if NET48_OR_GREATER

using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Web;
using System.Xml;




namespace mylogging.observability.Framework
    {
    /// <summary>
    /// Custom HTTP Module for OpenTelemetry WCF instrumentation
    /// Provides full distributed tracing capabilities for WCF services with request/response logging
    /// </summary>
    public class WcfTelemetryHttpModule : IHttpModule
    {
        private static readonly ActivitySource ActivitySource = new ActivitySource("WCF.Custom.Telemetry", "1.0.0");
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;
        private const string ActivityKey = "WcfTelemetry.Activity";
        private const string RequestBodyKey = "WcfTelemetry.RequestBody";
        private const string ResponseFilterKey = "WcfTelemetry.ResponseFilter";
        private const string ResponseBodyKey = "WcfTelemetry.ResponseBody";

        /// <summary>
        /// Gets or sets a value indicating whether request body should be logged
        /// </summary>
        public static bool LogRequestBody { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether response body should be logged
        /// </summary>
        public static bool LogResponseBody { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum size of body content to log (default 10KB)
        /// </summary>
        public static int MaxBodyLogSize { get; set; } = 10000; // 10KB default

        /// <summary>
        /// Gets or sets a value indicating whether sensitive data should be sanitized
        /// </summary>
        public static bool SanitizeSensitiveData { get; set; } = true;

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

            // Only process WCF requests (typically .svc endpoints)
            //if (!IsWcfRequest(context))
            //    return;

            try
            {
                // Extract trace context from incoming request
                var parentContext = Propagator.Extract(default, context.Request, ExtractTraceContext);
                Baggage.Current = parentContext.Baggage;


                // Capture request body before it's consumed
                string? requestBody = null;
                if (LogRequestBody && context.Request.InputStream.CanRead)
                {
                    requestBody = CaptureRequestBody(context.Request);
                    context.Items[RequestBodyKey] = requestBody;
                }

                // Parse WCF-specific information
                var wcfInfo = ExtractWcfInformation(context, requestBody);

                // Start activity with extracted context
                var activity = ActivitySource.StartActivity(
                    wcfInfo.OperationName ?? "WCF.Request",
                    ActivityKind.Server,
                    parentContext.ActivityContext);

                if (activity != null)
                {
                    // Set standard attributes
                    activity.SetTag("rpc.system", "wcf");
                    activity.SetTag("rpc.service", wcfInfo.ServiceName);
                    activity.SetTag("rpc.method", wcfInfo.OperationName);
                    activity.SetTag("http.method", context.Request.HttpMethod);
                    activity.SetTag("http.url", context.Request.Url.ToString());
                    activity.SetTag("http.target", context.Request.RawUrl);
                    activity.SetTag("http.host", context.Request.Url.Host);
                    activity.SetTag("http.scheme", context.Request.Url.Scheme);
                    activity.SetTag("net.host.name", context.Request.Url.Host);
                    activity.SetTag("net.host.port", context.Request.Url.Port);

                    // WCF-specific attributes
                    activity.SetTag("wcf.binding", wcfInfo.Binding);
                    activity.SetTag("wcf.action", wcfInfo.SoapAction);
                    activity.SetTag("wcf.contract", wcfInfo.ContractName);

                    if (!string.IsNullOrEmpty(context.Request.UserAgent))
                        activity.SetTag("http.user_agent", context.Request.UserAgent);

                    if (!string.IsNullOrEmpty(context.Request.UserHostAddress))
                        activity.SetTag("net.peer.ip", context.Request.UserHostAddress);

                    // Log request body size
                    if (!string.IsNullOrEmpty(requestBody))
                    {
                        activity.SetTag("http.request_content_length", requestBody.Length);
                    }

                    // Store activity in context for later retrieval
                    context.Items[ActivityKey] = activity;

                    // Install response filter to capture response body
                    if (LogResponseBody && context.Response.Filter != null)
                    {
                        var responseFilter = new ResponseCaptureFilter(context.Response.Filter, context);
                        context.Response.Filter = responseFilter;
                        context.Items[ResponseFilterKey] = responseFilter;
                    }

                    // Add custom enrichment hook
                    EnrichActivity(activity, context, "OnBeginRequest");
                }
            }
            catch (Exception ex)
            {
                // Log error but don't break the request pipeline
                Trace.TraceError($"WcfTelemetryHttpModule.OnBeginRequest error: {ex}");
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

                // Capture request body from context
                var requestBody = context.Items[RequestBodyKey] as string;

                // Capture response body from filter
                string? responseBody = null;
                var responseFilter = context.Items[ResponseFilterKey] as ResponseCaptureFilter;
                if (responseFilter != null)
                {
                    responseBody = responseFilter.GetCapturedContent();
                }

                // Log request/response bodies
                if (LogRequestBody && !string.IsNullOrEmpty(requestBody))
                {
                    LogRequestToActivity(activity, requestBody, context);
                }

                if (LogResponseBody && !string.IsNullOrEmpty(responseBody))
                {
                    LogResponseToActivity(activity, responseBody, context);
                }

                // Determine activity status based on response
                if (context.Response.StatusCode >= 400)
                {
                    activity.SetStatus(ActivityStatusCode.Error, $"HTTP {context.Response.StatusCode}");
                }
                else
                {
                    activity.SetStatus(ActivityStatusCode.Ok);
                }

                // Check for SOAP faults in response
                if (!string.IsNullOrEmpty(responseBody) &&
                    context.Response.ContentType?.Contains("xml") == true)
                {
                    CheckForSoapFault(activity, responseBody);
                }

                // Add custom enrichment hook
                EnrichActivity(activity, context, "OnEndRequest");
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.AddException(ex);
            }
            finally
            {
                activity.Stop();
                activity.Dispose();
            }
        }

        private void OnError(object sender, EventArgs e)
        {
            var context = ((HttpApplication)sender).Context;
            var activity = context.Items[ActivityKey] as Activity;

            if (activity == null)
                return;

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

                // Check for FaultException
                if (exception is FaultException faultException)
                {
                    activity.SetTag("wcf.fault.code", faultException.Code?.Name);
                    activity.SetTag("wcf.fault.reason", faultException.Reason?.ToString());
                }
            }
        }

        private bool IsWcfRequest(HttpContext context)
        {
            // Check if it's a .svc endpoint or has SOAP content type
            return context.Request.Path.Contains(".svc") ||
                   context.Request.ContentType?.Contains("soap") == true ||
                   context.Request.Headers["SOAPAction"] != null;
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

        private void LogRequestToActivity(Activity activity, string requestBody, HttpContext context)
        {
            try
            {
                // Truncate if too large
                var logBody = requestBody.Length > MaxBodyLogSize
                    ? requestBody.Substring(0, MaxBodyLogSize) + "... [truncated]"
                    : requestBody;

                // Sanitize sensitive data if enabled
                if (SanitizeSensitiveData)
                {
                    logBody = SanitizeXml(logBody);
                }

                // Add as activity event with the body
                var eventTags = new ActivityTagsCollection
                    {
                        { "message.type", "request" },
                        { "message.size", requestBody.Length },
                        { "message.content_type", context.Request.ContentType }
                    };

                activity.AddEvent(new ActivityEvent("wcf.request", DateTimeOffset.UtcNow, eventTags));

                // Store full body as tag (can be expensive, consider carefully)
                activity.SetTag("wcf.request.body", logBody);

                // Parse and extract parameter values if it's a SOAP request
                if (context.Request.ContentType?.Contains("soap") == true)
                {
                    ExtractSoapParameters(activity, requestBody, "request");
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Error logging request body: {ex.Message}");
            }
        }

        private void LogResponseToActivity(Activity activity, string responseBody, HttpContext context)
        {
            try
            {
                // Truncate if too large
                var logBody = responseBody.Length > MaxBodyLogSize
                    ? responseBody.Substring(0, MaxBodyLogSize) + "... [truncated]"
                    : responseBody;

                // Sanitize sensitive data if enabled
                if (SanitizeSensitiveData)
                {
                    logBody = SanitizeXml(logBody);
                }

                // Add as activity event
                var eventTags = new ActivityTagsCollection
                    {
                        { "message.type", "response" },
                        { "message.size", responseBody.Length },
                        { "message.content_type", context.Response.ContentType }
                    };

                activity.AddEvent(new ActivityEvent("wcf.response", DateTimeOffset.UtcNow, eventTags));

                // Store full body as tag
                activity.SetTag("wcf.response.body", logBody);
                activity.SetTag("http.response_content_length", responseBody.Length);

                // Parse and extract return values if it's a SOAP response
                if (context.Response.ContentType?.Contains("soap") == true)
                {
                    ExtractSoapParameters(activity, responseBody, "response");
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Error logging response body: {ex.Message}");
            }
        }

        private string SanitizeXml(string xml)
        {
            // List of common sensitive field names to redact
            var sensitiveFields = new[]
            {
                    "password", "pwd", "secret", "token", "apikey", "api_key",
                    "creditcard", "ssn", "authorization", "bearer"
                };

            foreach (var field in sensitiveFields)
            {
                // Simple regex replacement for XML elements (case-insensitive)
                var pattern = $@"(<{field}[^>]*>)(.*?)(</{field}>)";
                xml = System.Text.RegularExpressions.Regex.Replace(
                    xml,
                    pattern,
                    "$1***REDACTED***$3",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            return xml;
        }

        private void ExtractSoapParameters(Activity activity, string soapBody, string direction)
        {
            try
            {
                using (var stringReader = new StringReader(soapBody))
                using (var reader = XmlReader.Create(stringReader, new XmlReaderSettings
                {
                    IgnoreWhitespace = true,
                    IgnoreComments = true
                }))
                {
                    var parameters = new Dictionary<string, string>();
                    bool inBody = false;
                    bool inOperation = false;
                    string? currentElement = null;

                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            if (reader.LocalName == "Body")
                            {
                                inBody = true;
                            }
                            else if (inBody && !inOperation)
                            {
                                inOperation = true;
                                currentElement = reader.LocalName;
                            }
                            else if (inOperation && reader.Depth > 2)
                            {
                                currentElement = reader.LocalName;
                            }
                        }
                        else if (reader.NodeType == XmlNodeType.Text && inOperation && currentElement != null)
                        {
                            var value = reader.Value;
                            if (!string.IsNullOrWhiteSpace(value) && value.Length < 1000)
                            {
                                parameters[currentElement] = value;
                            }
                        }
                    }

                    // Add parameters as tags
                    foreach (var param in parameters.Take(10)) // Limit to 10 parameters
                    {
                        activity.SetTag($"wcf.{direction}.param.{param.Key}", param.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Error extracting SOAP parameters: {ex.Message}");
            }
        }

        private IEnumerable<string> ExtractTraceContext(HttpRequest request, string key)
        {
            // Extract from HTTP headers
            var values = request.Headers.GetValues(key);
            return values ?? Enumerable.Empty<string>();
        }

        private WcfRequestInfo ExtractWcfInformation(HttpContext context, string? requestBody)
        {
            var info = new WcfRequestInfo();

            try
            {
                // Extract from URL
                var path = context.Request.Path;
                if (path.EndsWith(".svc", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = path.Split('/');
                    info.ServiceName = parts.LastOrDefault()?.Replace(".svc", "") ?? string.Empty;
                }

                // Extract SOAP Action
                info.SoapAction = context.Request.Headers["SOAPAction"]?.Trim('"') ?? string.Empty;

                // Try to parse operation name from SOAP action
                if (!string.IsNullOrEmpty(info.SoapAction))
                {
                    var lastSlash = info.SoapAction.LastIndexOf('/');
                    if (lastSlash >= 0)
                    {
                        info.OperationName = info.SoapAction.Substring(lastSlash + 1);
                    }
                }

                // Detect binding type from request
                if (context.Request.Url.Scheme == "https")
                    info.Binding = "BasicHttpsBinding";
                else if (context.Request.ContentType?.Contains("soap") == true)
                    info.Binding = "BasicHttpBinding";
                else
                    info.Binding = "WebHttpBinding";

                // Try to extract more details from SOAP envelope (use captured body)
                if (!string.IsNullOrEmpty(requestBody) &&
                    context.Request.ContentType?.Contains("soap") == true)
                {
                    using (var stringReader = new StringReader(requestBody))
                    using (var reader = XmlReader.Create(stringReader, new XmlReaderSettings
                    {
                        IgnoreWhitespace = true,
                        IgnoreComments = true
                    }))
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element &&
                                reader.LocalName != "Envelope" &&
                                reader.LocalName != "Header" &&
                                reader.LocalName != "Body")
                            {
                                info.OperationName = reader.LocalName;
                                info.ContractName = reader.NamespaceURI;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Error extracting WCF information: {ex.Message}");
            }

            return info;
        }

        private void CheckForSoapFault(Activity activity, string responseBody)
        {
            try
            {
                if (responseBody.Contains("Fault") || responseBody.Contains("fault"))
                {
                    using (var stringReader = new StringReader(responseBody))
                    using (var reader = XmlReader.Create(stringReader))
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element &&
                                (reader.LocalName == "Fault" || reader.LocalName == "fault"))
                            {
                                activity.SetTag("wcf.soap_fault", true);
                                activity.SetStatus(ActivityStatusCode.Error, "SOAP Fault");

                                // Try to extract fault details
                                var faultDoc = new XmlDocument();
                                faultDoc.Load(new StringReader(responseBody));
                                var faultCode = faultDoc.SelectSingleNode("//*[local-name()='faultcode']")?.InnerText;
                                var faultString = faultDoc.SelectSingleNode("//*[local-name()='faultstring']")?.InnerText;

                                if (!string.IsNullOrEmpty(faultCode))
                                    activity.SetTag("wcf.fault.code", faultCode);
                                if (!string.IsNullOrEmpty(faultString))
                                    activity.SetTag("wcf.fault.string", faultString);

                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Error checking for SOAP fault: {ex.Message}");
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
            // Cleanup if needed
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