using System.Diagnostics;

namespace mylogging.observability
{
    /// <summary>
    /// Provides tracing capabilities for distributed telemetry.
    /// </summary>
    public interface ITracingService
    {
        /// <summary>
        /// Starts a new activity with the specified name and kind.
        /// </summary>
        /// <param name="name">The name of the activity.</param>
        /// <param name="kind">The kind of activity.</param>
        /// <returns>The started activity, or null if tracing is not enabled.</returns>
        Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal);

        /// <summary>
        /// Starts a new activity with the specified name, kind, and parent context.
        /// </summary>
        /// <param name="name">The name of the activity.</param>
        /// <param name="kind">The kind of activity.</param>
        /// <param name="parentContext">The parent activity context.</param>
        /// <returns>The started activity, or null if tracing is not enabled.</returns>
        Activity? StartActivity(string name, ActivityKind kind, ActivityContext parentContext);

        /// <summary>
        /// Starts a new activity with the specified name, kind, and parent ID.
        /// </summary>
        /// <param name="name">The name of the activity.</param>
        /// <param name="kind">The kind of activity.</param>
        /// <param name="parentId">The parent activity ID.</param>
        /// <returns>The started activity, or null if tracing is not enabled.</returns>
        Activity? StartActivity(string name, ActivityKind kind, string parentId);

        /// <summary>
        /// Adds a tag to the specified activity.
        /// </summary>
        /// <param name="activity">The activity to add the tag to.</param>
        /// <param name="key">The tag key.</param>
        /// <param name="value">The tag value.</param>
        void AddTag(Activity? activity, string key, object? value);

        /// <summary>
        /// Adds an event to the specified activity.
        /// </summary>
        /// <param name="activity">The activity to add the event to.</param>
        /// <param name="name">The name of the event.</param>
        /// <param name="attributes">Optional attributes for the event.</param>
        void AddEvent(Activity? activity, string name, Dictionary<string, object?>? attributes = null);

        /// <summary>
        /// Sets the status of the specified activity.
        /// </summary>
        /// <param name="activity">The activity to set the status for.</param>
        /// <param name="status">The status code.</param>
        /// <param name="description">Optional status description.</param>
        void SetStatus(Activity? activity, ActivityStatusCode status, string? description = null);

        /// <summary>
        /// Records an exception in the specified activity.
        /// </summary>
        /// <param name="activity">The activity to record the exception in.</param>
        /// <param name="exception">The exception to record.</param>
        void RecordException(Activity? activity, Exception exception);
    }

    /// <summary>
    /// Implementation of <see cref="ITracingService"/> for distributed tracing.
    /// </summary>
    public class TracingService : ITracingService
    {
        private readonly ActivitySource _activitySource;
        private readonly ObservabilityOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="TracingService"/> class.
        /// </summary>
        /// <param name="activitySource">The activity source for creating activities.</param>
        /// <param name="options">The observability options.</param>
        public TracingService(ActivitySource activitySource, ObservabilityOptions options)
        {
            _activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
        {
            var activity = _activitySource.StartActivity(name, kind);

            // For .NET Framework, if no listener is present, try to create activity manually
            if (activity == null)
            {
                // Create activity manually if no listener is subscribed
                activity = new Activity(name);
                var currentId = Activity.Current?.Id;
                if (currentId != null)
                {
                    activity.SetParentId(currentId);
                }
                activity.Start();
            }

            EnrichActivity(activity);
            return activity;
        }

        /// <inheritdoc/>
        public Activity? StartActivity(string name, ActivityKind kind, ActivityContext parentContext)
        {
            var activity = _activitySource.StartActivity(name, kind, parentContext);

            // For .NET Framework, if no listener is present, try to create activity manually
            if (activity == null)
            {
                // Create activity manually if no listener is subscribed
                activity = new Activity(name);
                activity.SetParentId(parentContext.TraceId.ToString());
                activity.Start();
            }

            EnrichActivity(activity);
            return activity;
        }

        /// <inheritdoc/>
        public Activity? StartActivity(string name, ActivityKind kind, string parentId)
        {
            var activity = _activitySource.StartActivity(name, kind, parentId);

            // For .NET Framework, if no listener is present, try to create activity manually
            if (activity == null)
            {
                // Create activity manually if no listener is subscribed
                activity = new Activity(name);
                if (!string.IsNullOrEmpty(parentId))
                    activity.SetParentId(parentId);
                activity.Start();
            }

            EnrichActivity(activity);
            return activity;
        }

        /// <inheritdoc/>
        public void AddTag(Activity? activity, string key, object? value)
        {
            if (activity != null && !string.IsNullOrEmpty(key) && value != null)
            {
                activity.SetTag(key, value.ToString());
            }
        }

        /// <inheritdoc/>
        public void AddEvent(Activity? activity, string name, Dictionary<string, object?>? attributes = null)
        {
            if (activity != null && !string.IsNullOrEmpty(name))
            {
                if (attributes == null || attributes.Count == 0)
                {
                    activity.AddEvent(new ActivityEvent(name));
                }
                else
                {
                    var activityTags = new List<KeyValuePair<string, object?>>();
                    foreach (var attr in attributes)
                    {
                        if (!string.IsNullOrEmpty(attr.Key))
                        {
                            activityTags.Add(new KeyValuePair<string, object?>(attr.Key, attr.Value));
                        }
                    }
                    activity.AddEvent(new ActivityEvent(name, DateTimeOffset.UtcNow, new ActivityTagsCollection(activityTags)));
                }
            }
        }

        /// <inheritdoc/>
        public void SetStatus(Activity? activity, ActivityStatusCode status, string? description = null)
        {
            if (activity != null)
            {
                activity.SetStatus(status, description);
            }
        }

        /// <inheritdoc/>
        public void RecordException(Activity? activity, Exception exception)
        {
            if (activity != null && exception != null)
            {
                var attributes = new Dictionary<string, object?>
                {
                    ["exception.type"] = exception.GetType().FullName,
                    ["exception.message"] = exception.Message,
                    ["exception.stacktrace"] = exception.StackTrace
                };

                AddEvent(activity, "exception", attributes);
                SetStatus(activity, ActivityStatusCode.Error, exception.Message);
            }
        }

        private void EnrichActivity(Activity? activity)
        {
            if (activity == null) return;

            // Add service information
            activity.SetTag("service.name", _options.ServiceName);
            activity.SetTag("service.version", _options.ServiceVersion);

            if (!string.IsNullOrEmpty(_options.ServiceNamespace))
                activity.SetTag("service.namespace", _options.ServiceNamespace);

            if (!string.IsNullOrEmpty(_options.ServiceInstanceId))
                activity.SetTag("service.instance.id", _options.ServiceInstanceId);

            // Add custom service attributes
            foreach (var attribute in _options.ServiceAttributes)
            {
                activity.SetTag($"service.{attribute.Key}", attribute.Value);
            }
        }
    }

    /// <summary>
    /// Provides extension methods for <see cref="ITracingService"/>.
    /// </summary>
    public static class TracingServiceExtensions
    {
        /// <summary>
        /// Executes an asynchronous operation with automatic tracing.
        /// </summary>
        /// <typeparam name="T">The return type of the operation.</typeparam>
        /// <param name="tracingService">The tracing service.</param>
        /// <param name="operationName">The name of the operation.</param>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="kind">The kind of activity.</param>
        /// <returns>The result of the operation.</returns>
        public static async Task<T> TraceAsync<T>(this ITracingService tracingService, string operationName,
            Func<Activity?, Task<T>> operation, ActivityKind kind = ActivityKind.Internal)
        {
            using var activity = tracingService.StartActivity(operationName, kind);
            try
            {
                var result = await operation(activity);
                tracingService.SetStatus(activity, ActivityStatusCode.Ok);
                return result;
            }
            catch (Exception ex)
            {
                tracingService.RecordException(activity, ex);
                throw;
            }
        }

        /// <summary>
        /// Executes an asynchronous operation with automatic tracing.
        /// </summary>
        /// <param name="tracingService">The tracing service.</param>
        /// <param name="operationName">The name of the operation.</param>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="kind">The kind of activity.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task TraceAsync(this ITracingService tracingService, string operationName,
            Func<Activity?, Task> operation, ActivityKind kind = ActivityKind.Internal)
        {
            using var activity = tracingService.StartActivity(operationName, kind);
            try
            {
                await operation(activity);
                tracingService.SetStatus(activity, ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                tracingService.RecordException(activity, ex);
                throw;
            }
        }

        /// <summary>
        /// Executes a synchronous operation with automatic tracing.
        /// </summary>
        /// <typeparam name="T">The return type of the operation.</typeparam>
        /// <param name="tracingService">The tracing service.</param>
        /// <param name="operationName">The name of the operation.</param>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="kind">The kind of activity.</param>
        /// <returns>The result of the operation.</returns>
        public static T Trace<T>(this ITracingService tracingService, string operationName,
            Func<Activity?, T> operation, ActivityKind kind = ActivityKind.Internal)
        {
            using var activity = tracingService.StartActivity(operationName, kind);
            try
            {
                var result = operation(activity);
                tracingService.SetStatus(activity, ActivityStatusCode.Ok);
                return result;
            }
            catch (Exception ex)
            {
                tracingService.RecordException(activity, ex);
                throw;
            }
        }

        /// <summary>
        /// Executes a synchronous operation with automatic tracing.
        /// </summary>
        /// <param name="tracingService">The tracing service.</param>
        /// <param name="operationName">The name of the operation.</param>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="kind">The kind of activity.</param>
        public static void Trace(this ITracingService tracingService, string operationName,
            Action<Activity?> operation, ActivityKind kind = ActivityKind.Internal)
        {
            using var activity = tracingService.StartActivity(operationName, kind);
            try
            {
                operation(activity);
                tracingService.SetStatus(activity, ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                tracingService.RecordException(activity, ex);
                throw;
            }
        }
    }
}
