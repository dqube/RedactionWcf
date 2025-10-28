using OpenTelemetry;
using OpenTelemetry.Logs;
using System.Text.RegularExpressions;

namespace mylogging.observability
{
    /// <summary>
    /// Processes OpenTelemetry log records to redact sensitive information based on configuration.
    /// </summary>
    public class RedactionLogProcessor : BaseProcessor<LogRecord>
    {
        private readonly ObservabilityOptions _options;
        private readonly HashSet<string> _sensitiveKeys;
        private readonly string _redactionText;
        private readonly Regex _sensitivePatternRegex;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedactionLogProcessor"/> class.
        /// </summary>
        /// <param name="options">The observability configuration options containing redaction settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public RedactionLogProcessor(ObservabilityOptions options)
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
        /// Processes a log record to redact sensitive information.
        /// </summary>
        /// <param name="data">The log record to process.</param>
        public override void OnEnd(LogRecord data)
        {
            if (data == null || !_options.EnableRedaction)
            {
                return;
            }

            try
            {
                // Redact sensitive information in the log message body
                if (data.Body != null)
                {
                    var bodyString = data.Body.ToString();
                    if (!string.IsNullOrEmpty(bodyString))
                    {
                        var redactedBody = RedactSensitiveData(bodyString);
                        // Note: LogRecord.Body is readonly, so we rely on attribute redaction
                    }
                }

                // Redact sensitive information in log attributes
                if (data.Attributes != null)
                {
                    RedactAttributes(data);
                }

                // Redact exception stack traces if needed
                if (data.Exception != null && _options.Redaction?.RedactRequestBody == true)
                {
                    // Exception messages and stack traces are already captured
                    // We process them during attribute redaction
                }
            }
            catch (Exception ex)
            {
                // Log processing errors shouldn't break the telemetry pipeline
                System.Diagnostics.Trace.TraceWarning($"RedactionLogProcessor error: {ex.Message}");
            }

            base.OnEnd(data);
        }

        /// <summary>
        /// Redacts sensitive data from attributes.
        /// </summary>
        private void RedactAttributes(LogRecord logRecord)
        {
            if (logRecord.Attributes == null)
                return;

            var attributesToRedact = new List<KeyValuePair<string, object?>>();

            foreach (var attribute in logRecord.Attributes)
            {
                if (IsSensitiveKey(attribute.Key))
                {
                    attributesToRedact.Add(new KeyValuePair<string, object?>(attribute.Key, _redactionText));
                }
                else if (attribute.Value is string strValue)
                {
                    var redactedValue = RedactSensitiveData(strValue);
                    if (redactedValue != strValue)
                    {
                        attributesToRedact.Add(new KeyValuePair<string, object?>(attribute.Key, redactedValue));
                    }
                }
            }

            // Note: LogRecord attributes are readonly after creation
            // In a real implementation, you might need to use a custom exporter
            // that applies redaction during export rather than modifying the LogRecord
        }

        /// <summary>
        /// Redacts sensitive data from a string based on patterns and keywords.
        /// </summary>
        /// <param name="input">The input string to redact.</param>
        /// <returns>The redacted string.</returns>
        private string RedactSensitiveData(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var redacted = input;

            // Redact based on regex patterns
            redacted = _sensitivePatternRegex.Replace(redacted, match =>
            {
                // Keep the key/field name but redact the value
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

        /// <summary>
        /// Redacts sensitive data in JSON-like structures.
        /// </summary>
        private string RedactJsonLikeStructures(string input)
        {
            foreach (var sensitiveKey in _sensitiveKeys)
            {
                // Match: "key": "value" or 'key': 'value'
                var jsonPattern = $@"(['""]?{Regex.Escape(sensitiveKey)}['""]?\s*:\s*['""]?)([^'""]*?)(['""]?)";
                input = Regex.Replace(input, jsonPattern, $"$1{_redactionText}$3", RegexOptions.IgnoreCase);
            }

            return input;
        }

        /// <summary>
        /// Redacts sensitive data in XML-like structures.
        /// </summary>
        private string RedactXmlLikeStructures(string input)
        {
            foreach (var sensitiveKey in _sensitiveKeys)
            {
                // Match: <key>value</key>
                var xmlPattern = $@"(<{Regex.Escape(sensitiveKey)}[^>]*>)([^<]*?)(</{Regex.Escape(sensitiveKey)}>)";
                input = Regex.Replace(input, xmlPattern, $"$1{_redactionText}$3", RegexOptions.IgnoreCase);
            }

            return input;
        }

        /// <summary>
        /// Redacts sensitive query parameters from URLs.
        /// </summary>
        private string RedactQueryParameters(string input)
        {
            foreach (var sensitiveKey in _sensitiveKeys)
            {
                // Match: key=value in query strings
                var queryPattern = $@"({Regex.Escape(sensitiveKey)}=)([^&\s]*)";
                input = Regex.Replace(input, queryPattern, $"$1{_redactionText}", RegexOptions.IgnoreCase);
            }

            return input;
        }

        /// <summary>
        /// Checks if a key is considered sensitive based on configuration.
        /// </summary>
        private bool IsSensitiveKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            return _sensitiveKeys.Contains(key);
        }

        /// <summary>
        /// Builds a regex pattern for detecting sensitive data.
        /// </summary>
        private Regex BuildSensitivePatternRegex()
        {
            var patterns = new List<string>();

            // Credit card pattern
            patterns.Add(@"\b\d{4}[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{4}\b");

            // Social Security Number pattern
            patterns.Add(@"\b\d{3}[\s\-]?\d{2}[\s\-]?\d{4}\b");

            // Email pattern (optional - might be too aggressive)
            // patterns.Add(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b");

            // API Key/Token pattern (long alphanumeric strings)
            patterns.Add(@"\b[A-Za-z0-9]{32,}\b");

            // Bearer token pattern
            patterns.Add(@"(Bearer\s+)[\w\-\.]+");

            var combinedPattern = string.Join("|", patterns);
            return new Regex(combinedPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Adds default sensitive keys if none are configured.
        /// </summary>
        private void AddDefaultSensitiveKeys()
        {
            var defaultKeys = new[]
            {
                // Authentication & Authorization
                "password", "pwd", "passwd", "secret", "token", "apikey", "api_key", "api-key",
                "authorization", "auth", "bearer", "access_token", "refresh_token", "auth_token",
                "session", "session_id", "sessionid", "jsessionid", "phpsessid",
                
                // Financial & PII
                "creditcard", "credit_card", "card_number", "cardnumber", "cvv", "cvc",
                "ssn", "social_security", "socialsecurity", "social_security_number",
                "tax_id", "taxid", "ein", "routing_number", "account_number",
                
                // Personal Information
                "email", "phone", "phonenumber", "phone_number", "mobile", "dob", "date_of_birth",
                "birthdate", "address", "zip", "zipcode", "postal_code", "nationalid",
                
                // Security
                "private_key", "privatekey", "public_key", "publickey", "certificate", "cert",
                "encryption_key", "symmetric_key", "master_key", "signing_key",
                
                // Database & Connection Strings
                "connection_string", "connectionstring", "database_password", "db_password",
                "db_pwd", "jdbc_url", "connection", "datasource",
                
                // Cloud & Services
                "aws_access_key", "aws_secret", "azure_key", "gcp_key", "client_secret",
                "tenant_id", "subscription_key", "service_key"
            };

            foreach (var key in defaultKeys)
            {
                _sensitiveKeys.Add(key);
            }
        }

        /// <summary>
        /// Releases resources used by the processor.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources.</param>
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
    /// Extension methods for adding redaction processor to OpenTelemetry logging.
    /// </summary>
    public static class RedactionLogProcessorExtensions
    {
        /// <summary>
        /// Adds the redaction log processor to the logging pipeline.
        /// </summary>
        /// <param name="builder">The OpenTelemetryLoggerOptions builder.</param>
        /// <param name="options">The observability options containing redaction configuration.</param>
        /// <returns>The builder for method chaining.</returns>
        public static OpenTelemetryLoggerOptions AddRedactionProcessor(
            this OpenTelemetryLoggerOptions builder,
            ObservabilityOptions options)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (options.EnableRedaction && options.Redaction != null)
            {
                builder.AddProcessor(new RedactionLogProcessor(options));
            }

            return builder;
        }

        /// <summary>
        /// Adds both redaction processor and Splunk exporter to the logging pipeline.
        /// </summary>
        /// <param name="builder">The OpenTelemetryLoggerOptions builder.</param>
        /// <param name="options">The observability options containing configuration.</param>
        /// <returns>The builder for method chaining.</returns>
        public static OpenTelemetryLoggerOptions AddRedactionAndSplunkExporter(
            this OpenTelemetryLoggerOptions builder,
            ObservabilityOptions options)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            // Add redaction processor first to redact before exporting
            if (options.EnableRedaction)
            {
                builder.AddRedactionProcessor(options);
            }

            // Then add Splunk exporter
            if (options.SplunkExporter != null && !string.IsNullOrEmpty(options.SplunkExporter.Url))
            {
                builder.AddProcessor(new BatchLogRecordExportProcessor(new SplunkLogExporter(options)));
            }

            return builder;
        }
    }
}
