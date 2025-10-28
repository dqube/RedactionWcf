using OpenTelemetry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace mylogging.observability
{
    /// <summary>
    /// Processes OpenTelemetry Activity traces to redact sensitive information based on configuration.
    /// </summary>
    public class RedactionActivityProcessor : BaseProcessor<Activity>
    {
        private readonly ObservabilityOptions _options;
        private readonly HashSet<string> _sensitiveKeys;
        private readonly string _redactionText;
        private readonly Regex _sensitivePatternRegex;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedactionActivityProcessor"/> class.
        /// </summary>
        /// <param name="options">The observability configuration options containing redaction settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public RedactionActivityProcessor(ObservabilityOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _redactionText = _options.Redaction?.RedactionText ?? "[REDACTED]";
            
            // Initialize sensitive keys from configuration
            _sensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (_options.Redaction?.SensitiveKeys != null && _options.Redaction.SensitiveKeys.Any())
            {
                foreach (var key in _options.Redaction.SensitiveKeys)
                {
                    _sensitiveKeys.Add(key);
                }
            }
            else
            {
                // Default sensitive keys if none configured
                AddDefaultSensitiveKeys();
            }

            // Build regex pattern for sensitive data detection
            _sensitivePatternRegex = BuildSensitivePatternRegex();
        }

        /// <summary>
        /// Processes an activity to redact sensitive information.
        /// </summary>
        /// <param name="data">The activity to process.</param>
        public override void OnEnd(Activity data)
        {
            if (data == null || !_options.EnableRedaction)
            {
                base.OnEnd(data);
                return;
            }

            try
            {
                // Redact sensitive information in activity tags
                if (data.TagObjects != null)
                {
                    RedactActivityTags(data);
                }

                // Redact sensitive information in baggage
                if (data.Baggage != null)
                {
                    RedactActivityBaggage(data);
                }

                // Redact sensitive information in events
                if (data.Events != null)
                {
                    foreach (var activityEvent in data.Events)
                    {
                        RedactActivityEvent(activityEvent);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log processing errors shouldn't break the telemetry pipeline
                System.Diagnostics.Trace.TraceWarning($"RedactionActivityProcessor error: {ex.Message}");
            }

            base.OnEnd(data);
        }

        /// <summary>
        /// Redacts sensitive data from activity tags.
        /// </summary>
        private void RedactActivityTags(Activity activity)
        {
            var tagsToRedact = new List<KeyValuePair<string, object?>>();

            foreach (var tag in activity.TagObjects)
            {
                if (IsSensitiveKey(tag.Key))
                {
                    tagsToRedact.Add(new KeyValuePair<string, object?>(tag.Key, _redactionText));
                }
                else if (tag.Value is string strValue)
                {
                    var redactedValue = RedactSensitiveData(strValue);
                    if (redactedValue != strValue)
                    {
                        tagsToRedact.Add(new KeyValuePair<string, object?>(tag.Key, redactedValue));
                    }
                }
            }

            // Apply redacted tags
            foreach (var tag in tagsToRedact)
            {
                activity.SetTag(tag.Key, tag.Value);
            }
        }

        /// <summary>
        /// Redacts sensitive data from activity baggage.
        /// </summary>
        private void RedactActivityBaggage(Activity activity)
        {
            var baggageToRedact = new List<KeyValuePair<string, string?>>();

            foreach (var baggage in activity.Baggage)
            {
                if (IsSensitiveKey(baggage.Key))
                {
                    baggageToRedact.Add(new KeyValuePair<string, string?>(baggage.Key, _redactionText));
                }
                else if (baggage.Value != null)
                {
                    var redactedValue = RedactSensitiveData(baggage.Value);
                    if (redactedValue != baggage.Value)
                    {
                        baggageToRedact.Add(new KeyValuePair<string, string?>(baggage.Key, redactedValue));
                    }
                }
            }

            // Apply redacted baggage
            foreach (var baggage in baggageToRedact)
            {
                activity.SetBaggage(baggage.Key, baggage.Value);
            }
        }

        /// <summary>
        /// Redacts sensitive data from activity events.
        /// </summary>
        private void RedactActivityEvent(ActivityEvent activityEvent)
        {
            // Note: ActivityEvent tags are readonly, so we can only process them during export
            // The actual redaction will happen in the exporter
        }

        /// <summary>
        /// Redacts sensitive data from a string based on patterns and keywords.
        /// </summary>
        private string RedactSensitiveData(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var redacted = input;

            // Redact based on regex patterns
            redacted = _sensitivePatternRegex.Replace(redacted, match =>
            {
                if (match.Groups.Count > 1)
                {
                    return $"{match.Groups[1].Value}{_redactionText}";
                }
                return _redactionText;
            });

            // Redact JSON-like structures
            redacted = RedactJsonLikeStructures(redacted);

            // Redact XML-like structures
            redacted = RedactXmlLikeStructures(redacted);

            // Redact query parameters
            if (_options.Redaction?.RedactQueryParams == true)
            {
                redacted = RedactQueryParameters(redacted);
            }

            return redacted;
        }

        private string RedactJsonLikeStructures(string input)
        {
            foreach (var sensitiveKey in _sensitiveKeys)
            {
                var jsonPattern = $@"(['""]?{Regex.Escape(sensitiveKey)}['""]?\s*:\s*['""]?)([^'""]*?)(['""]?)";
                input = Regex.Replace(input, jsonPattern, $"$1{_redactionText}$3", RegexOptions.IgnoreCase);
            }
            return input;
        }

        private string RedactXmlLikeStructures(string input)
        {
            foreach (var sensitiveKey in _sensitiveKeys)
            {
                var xmlPattern = $@"(<{Regex.Escape(sensitiveKey)}[^>]*>)([^<]*?)(</{Regex.Escape(sensitiveKey)}>)";
                input = Regex.Replace(input, xmlPattern, $"$1{_redactionText}$3", RegexOptions.IgnoreCase);
            }
            return input;
        }

        private string RedactQueryParameters(string input)
        {
            foreach (var sensitiveKey in _sensitiveKeys)
            {
                var queryPattern = $@"({Regex.Escape(sensitiveKey)}=)([^&\s]*)";
                input = Regex.Replace(input, queryPattern, $"$1{_redactionText}", RegexOptions.IgnoreCase);
            }
            return input;
        }

        private bool IsSensitiveKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            return _sensitiveKeys.Contains(key);
        }

        private Regex BuildSensitivePatternRegex()
        {
            var patterns = new List<string>
            {
                @"\b\d{4}[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{4}\b",
                @"\b\d{3}[\s\-]?\d{2}[\s\-]?\d{4}\b",
                @"\b[A-Za-z0-9]{32,}\b",
                @"(Bearer\s+)[\w\-\.]+"
            };
            var combinedPattern = string.Join("|", patterns);
            return new Regex(combinedPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        private void AddDefaultSensitiveKeys()
        {
            var defaultKeys = new[]
            {
                "password", "pwd", "passwd", "secret", "token", "apikey", "api_key", "api-key",
                "authorization", "auth", "bearer", "access_token", "refresh_token", "auth_token",
                "session", "session_id", "sessionid", "jsessionid", "phpsessid",
                "creditcard", "credit_card", "card_number", "cardnumber", "cvv", "cvc",
                "ssn", "social_security", "socialsecurity", "social_security_number",
                "tax_id", "taxid", "ein", "routing_number", "account_number",
                "email", "phone", "phonenumber", "phone_number", "mobile", "dob", "date_of_birth",
                "birthdate", "address", "zip", "zipcode", "postal_code", "nationalid",
                "private_key", "privatekey", "public_key", "publickey", "certificate", "cert",
                "encryption_key", "symmetric_key", "master_key", "signing_key",
                "connection_string", "connectionstring", "database_password", "db_password",
                "db_pwd", "jdbc_url", "connection", "datasource",
                "aws_access_key", "aws_secret", "azure_key", "gcp_key", "client_secret",
                "tenant_id", "subscription_key", "service_key"
            };
            foreach (var key in defaultKeys)
            {
                _sensitiveKeys.Add(key);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Cleanup if needed
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Exports OpenTelemetry Activity traces to Splunk HTTP Event Collector (HEC).
    /// </summary>
    public class SplunkTraceExporter : BaseExporter<Activity>
    {
        private readonly HttpClient _httpClient;
        private readonly string _endpoint;
        private readonly string _token;
        private readonly ObservabilityOptions _options;

        public SplunkTraceExporter(ObservabilityOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (options.SplunkExporter == null)
                throw new ArgumentException("SplunkExporter configuration cannot be null", nameof(options));

            var endpoint = options.SplunkExporter.Url;
            var token = options.SplunkExporter.Token;

            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint cannot be null or empty", nameof(options));
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Token cannot be null or empty", nameof(options));

            _endpoint = endpoint;
            _token = token;
            _options = options;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Splunk {_token}");
        }

        public override ExportResult Export(in Batch<Activity> batch)
        {
            try
            {
                var events = new List<object>();
                foreach (var activity in batch)
                {
                    var splunkEvent = new
                    {
                        time = new DateTimeOffset(activity.StartTimeUtc).ToUnixTimeSeconds(),
                        host = _options.SplunkExporter?.Host ?? Environment.MachineName,
                        source = _options.SplunkExporter?.Source ?? _options.ServiceName,
                        sourcetype = _options.SplunkExporter?.Sourcetype ?? "_json",
                        index = _options.SplunkExporter?.Index ?? "main",
                        @event = new
                        {
                            traceId = activity.TraceId.ToString(),
                            spanId = activity.SpanId.ToString(),
                            parentSpanId = activity.ParentSpanId.ToString(),
                            operationName = activity.OperationName,
                            displayName = activity.DisplayName,
                            kind = activity.Kind.ToString(),
                            status = activity.Status.ToString(),
                            statusDescription = activity.StatusDescription,
                            startTime = activity.StartTimeUtc,
                            duration = activity.Duration.TotalMilliseconds,
                            tags = GetTags(activity),
                            baggage = GetBaggage(activity),
                            events = GetEvents(activity),
                            links = GetLinks(activity),
                            serviceName = _options.ServiceName,
                            serviceVersion = _options.ServiceVersion
                        }
                    };
                    events.Add(splunkEvent);
                }

                var jsonPayload = JsonSerializer.Serialize(events);
                var content = new StringContent(jsonPayload, Encoding.UTF8);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var response = _httpClient.PostAsync(_endpoint, content).GetAwaiter().GetResult();
                return response.IsSuccessStatusCode ? ExportResult.Success : ExportResult.Failure;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"SplunkTraceExporter error: {ex.Message}");
                return ExportResult.Failure;
            }
        }

        private Dictionary<string, object?> GetTags(Activity activity)
        {
            var tags = new Dictionary<string, object?>();
            if (activity.TagObjects != null)
            {
                foreach (var tag in activity.TagObjects)
                {
                    tags[tag.Key] = tag.Value?.ToString() ?? string.Empty;
                }
            }
            return tags;
        }

        private Dictionary<string, string?> GetBaggage(Activity activity)
        {
            var baggage = new Dictionary<string, string?>();
            if (activity.Baggage != null)
            {
                foreach (var item in activity.Baggage)
                {
                    baggage[item.Key] = item.Value;
                }
            }
            return baggage;
        }

        private List<object> GetEvents(Activity activity)
        {
            var events = new List<object>();
            if (activity.Events != null)
            {
                foreach (var activityEvent in activity.Events)
                {
                    var eventTags = new Dictionary<string, object?>();
                    if (activityEvent.Tags != null)
                    {
                        foreach (var tag in activityEvent.Tags)
                        {
                            eventTags[tag.Key] = tag.Value?.ToString() ?? string.Empty;
                        }
                    }
                    events.Add(new
                    {
                        name = activityEvent.Name,
                        timestamp = activityEvent.Timestamp,
                        tags = eventTags
                    });
                }
            }
            return events;
        }

        private List<object> GetLinks(Activity activity)
        {
            var links = new List<object>();
            if (activity.Links != null)
            {
                foreach (var link in activity.Links)
                {
                    var linkTags = new Dictionary<string, object?>();
                    if (link.Tags != null)
                    {
                        foreach (var tag in link.Tags)
                        {
                            linkTags[tag.Key] = tag.Value?.ToString() ?? string.Empty;
                        }
                    }
                    links.Add(new
                    {
                        traceId = link.Context.TraceId.ToString(),
                        spanId = link.Context.SpanId.ToString(),
                        tags = linkTags
                    });
                }
            }
            return links;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
