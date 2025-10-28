using OpenTelemetry;
using OpenTelemetry.Logs;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace mylogging.observability.Common
{
    /// <summary>
    /// Exports OpenTelemetry log records to Splunk HTTP Event Collector (HEC).
    /// </summary>
    public class SplunkLogExporter : BaseExporter<LogRecord>
    {
        private readonly HttpClient _httpClient;
        private readonly string _endpoint;
        private readonly string _token;

        /// <summary>
        /// Initializes a new instance of the <see cref="SplunkLogExporter"/> class.
        /// </summary>
        /// <param name="options">The observability configuration options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        /// <exception cref="ArgumentException">Thrown when endpoint or token is null or empty.</exception>
        public SplunkLogExporter(ObservabilityOptions options)
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
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Splunk {_token}");
        }

        /// <summary>
        /// Exports a batch of log records to Splunk.
        /// </summary>
        /// <param name="batch">The batch of log records to export.</param>
        /// <returns>The export result indicating success or failure.</returns>
        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            try
            {
                var events = new List<object>();

                foreach (var logRecord in batch)
                {
                    var splunkEvent = new
                    {
                        time = new DateTimeOffset(logRecord.Timestamp).ToUnixTimeSeconds(),
                        host = Environment.MachineName,
                        source = logRecord.CategoryName,
                        sourcetype = "_json",
                        @event = new
                        {
                            message = logRecord.FormattedMessage ?? logRecord.Body?.ToString(),
                            level = logRecord.LogLevel.ToString(),
                            traceId = logRecord.TraceId.ToString(),
                            spanId = logRecord.SpanId.ToString(),
                            attributes = GetAttributes(logRecord),
                            exception = logRecord.Exception?.ToString()
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
                return ExportResult.Failure;
            }
        }

        private Dictionary<string, object> GetAttributes(LogRecord logRecord)
        {
            var attributes = new Dictionary<string, object>();

            if (logRecord.Attributes != null)
            {
                foreach (var attribute in logRecord.Attributes)
                {
                    attributes[attribute.Key] = attribute.Value?.ToString() ?? string.Empty;
                }
            }

            return attributes;
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="SplunkLogExporter"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
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
