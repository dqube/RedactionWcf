#if NET8_0_OR_GREATER

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using System;
using System.Collections.Generic;

namespace mylogging.observability.Core
{
    /// <summary>
    /// Extension methods for configuring observability in ASP.NET Core applications with fluent API.
    /// </summary>
    public static class ObservabilityServiceCollectionExtensions
    {
        /// <summary>
        /// Adds comprehensive observability including logging with Splunk exporter and redaction.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="options">The observability options to configure.</param>
        /// <returns>The service collection for method chaining.</returns>
        /// <example>
        /// <code>
        /// // In Program.cs:
        /// var options = new ObservabilityOptions
        /// {
        ///     ServiceName = "MyService",
        ///     ServiceVersion = "1.0.0",
        ///     EnableRedaction = true,
        ///     SplunkExporter = new SplunkExporter
        ///     {
        ///         Url = "http://localhost:8088/services/collector",
        ///         Token = "your-splunk-token"
        ///     }
        /// };
        /// builder.Services.AddObservability(options);
        /// </code>
        /// </example>
        public static IServiceCollection AddObservability(
            this IServiceCollection services,
            ObservabilityOptions options)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (options == null) throw new ArgumentNullException(nameof(options));

            // Register options as singleton
            services.AddSingleton(options);

            // Configure OpenTelemetry Logging with Splunk and Redaction
            services.AddLogging(logging =>
            {
                logging.AddOpenTelemetry(otel =>
                {
                    otel.SetResourceBuilder(CreateResourceBuilder(options));

                    // Add redaction processor first (before exporters)
                    if (options.EnableRedaction)
                    {
                        otel.AddRedactionProcessor(options);
                    }

                    // Add Splunk exporter
                    if (options.SplunkExporter != null && !string.IsNullOrEmpty(options.SplunkExporter.Url))
                    {
                        otel.AddProcessor(new BatchLogRecordExportProcessor(new SplunkLogExporter(options)));
                    }

                    // Configure log options
                    otel.IncludeFormattedMessage = true;
                    otel.IncludeScopes = true;
                });
            });

            return services;
        }

        /// <summary>
        /// Adds observability with Splunk exporter and redaction using fluent builder pattern.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Action to configure the observability builder.</param>
        /// <returns>The service collection for method chaining.</returns>
        /// <example>
        /// <code>
        /// // In Program.cs:
        /// builder.Services.AddObservabilityBuilder(obs => obs
        ///     .WithService("MyService", "1.0.0")
        ///     .WithSplunkExporter("http://localhost:8088/services/collector", "your-token")
        ///     .WithRedaction(redaction =>
        ///     {
        ///         redaction.RedactionText = "[MASKED]";
        ///         redaction.SensitiveKeys.AddRange(new[] { "ssn", "creditcard" });
        ///     })
        ///     .Build());
        /// </code>
        /// </example>
        public static IServiceCollection AddObservabilityBuilder(
            this IServiceCollection services,
            Action<ObservabilityBuilder> configure)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            var options = new ObservabilityOptions();
            var builder = new ObservabilityBuilder(services, options);
            configure(builder);

            return services;
        }

      
      

        private static ResourceBuilder CreateResourceBuilder(ObservabilityOptions options)
        {
            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddService(
                    serviceName: options.ServiceName,
                    serviceVersion: options.ServiceVersion,
                    serviceNamespace: options.ServiceNamespace,
                    serviceInstanceId: options.ServiceInstanceId);

            // Add custom service attributes
            if (options.ServiceAttributes != null && options.ServiceAttributes.Count > 0)
            {
                foreach (var attr in options.ServiceAttributes)
                {
                    resourceBuilder.AddAttributes(new[] { new KeyValuePair<string, object>(attr.Key, attr.Value) });
                }
            }

            return resourceBuilder;
        }
    }

    /// <summary>
    /// Fluent builder for configuring observability with method chaining.
    /// </summary>
    public class ObservabilityBuilder
    {
        private readonly IServiceCollection _services;
        private readonly ObservabilityOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObservabilityBuilder"/> class.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="options">The observability options.</param>
        public ObservabilityBuilder(IServiceCollection services, ObservabilityOptions options)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Configures the service information.
        /// </summary>
        /// <param name="name">The service name.</param>
        /// <param name="version">The service version.</param>
        /// <param name="namespace">The service namespace.</param>
        /// <returns>The builder for method chaining.</returns>
        public ObservabilityBuilder WithService(string name, string version = "1.0.0", string? @namespace = null)
        {
            _options.ServiceName = name;
            _options.ServiceVersion = version;
            _options.ServiceNamespace = @namespace;
            return this;
        }

        /// <summary>
        /// Configures Splunk exporter for logs.
        /// </summary>
        /// <param name="url">The Splunk HEC endpoint URL.</param>
        /// <param name="token">The Splunk HEC authentication token.</param>
        /// <param name="configure">Optional action to configure additional Splunk settings.</param>
        /// <returns>The builder for method chaining.</returns>
        public ObservabilityBuilder WithSplunkExporter(
            string url,
            string token,
            Action<SplunkExporter>? configure = null)
        {
            _options.SplunkExporter = new SplunkExporter
            {
                Url = url,
                Token = token,
                Source = _options.ServiceName,
                Sourcetype = "_json",
                Index = "main"
            };

            configure?.Invoke(_options.SplunkExporter);
            return this;
        }

        /// <summary>
        /// Enables sensitive data redaction.
        /// </summary>
        /// <param name="configure">Optional action to configure redaction settings.</param>
        /// <returns>The builder for method chaining.</returns>
        public ObservabilityBuilder WithRedaction(Action<RedactionOptions>? configure = null)
        {
            _options.EnableRedaction = true;
            
            if (_options.Redaction == null)
                _options.Redaction = new RedactionOptions();

            configure?.Invoke(_options.Redaction);
            return this;
        }

        /// <summary>
        /// Configures tracing options.
        /// </summary>
        /// <param name="configure">Action to configure tracing.</param>
        /// <returns>The builder for method chaining.</returns>
        public ObservabilityBuilder WithTracing(Action<TracingOptions> configure)
        {
            if (_options.Tracing == null)
                _options.Tracing = new TracingOptions();

            configure(_options.Tracing);
            return this;
        }

        /// <summary>
        /// Configures request/response logging options.
        /// </summary>
        /// <param name="configure">Action to configure request/response logging.</param>
        /// <returns>The builder for method chaining.</returns>
        public ObservabilityBuilder WithRequestResponseLogging(Action<RequestResponseLoggingOptions> configure)
        {
            if (_options.RequestResponseLogging == null)
                _options.RequestResponseLogging = new RequestResponseLoggingOptions();

            configure(_options.RequestResponseLogging);
            return this;
        }

        /// <summary>
        /// Adds a custom service attribute.
        /// </summary>
        /// <param name="key">The attribute key.</param>
        /// <param name="value">The attribute value.</param>
        /// <returns>The builder for method chaining.</returns>
        public ObservabilityBuilder WithServiceAttribute(string key, string value)
        {
            if (_options.ServiceAttributes == null)
                _options.ServiceAttributes = new Dictionary<string, string>();

            _options.ServiceAttributes[key] = value;
            return this;
        }

        /// <summary>
        /// Builds and registers the observability configuration.
        /// </summary>
        /// <returns>The service collection for method chaining.</returns>
        public IServiceCollection Build()
        {
            // Register options
            _services.AddSingleton(_options);

            // Configure logging with OpenTelemetry
            _services.AddLogging(logging =>
            {
                logging.AddOpenTelemetry(otel =>
                {
                    otel.SetResourceBuilder(CreateResourceBuilder());

                    // Add redaction processor first
                    if (_options.EnableRedaction)
                    {
                        otel.AddRedactionProcessor(_options);
                    }

                    // Add Splunk exporter
                    if (_options.SplunkExporter != null && !string.IsNullOrEmpty(_options.SplunkExporter.Url))
                    {
                        otel.AddProcessor(new SimpleLogRecordExportProcessor(new SplunkLogExporter(_options)));
                    }

                    otel.IncludeFormattedMessage = true;
                    otel.IncludeScopes = true;
                });
            });

            return _services;
        }

        private ResourceBuilder CreateResourceBuilder()
        {
            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddService(
                    serviceName: _options.ServiceName,
                    serviceVersion: _options.ServiceVersion,
                    serviceNamespace: _options.ServiceNamespace,
                    serviceInstanceId: _options.ServiceInstanceId);

            if (_options.ServiceAttributes != null && _options.ServiceAttributes.Count > 0)
            {
                foreach (var attr in _options.ServiceAttributes)
                {
                    resourceBuilder.AddAttributes(new[] { new KeyValuePair<string, object>(attr.Key, attr.Value) });
                }
            }

            return resourceBuilder;
        }
    }
}

#endif
