#if NET8_0
using Microsoft.AspNetCore.Builder;
using mylogging.observability;


namespace mylogging.Extensions
{
    /// <summary>
    /// Provides extension methods for <see cref="IApplicationBuilder"/> to add logging middleware.
    /// </summary>
    public static class ApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds request and response logging middleware to the application pipeline.
        /// </summary>
        /// <param name="app">The <see cref="IApplicationBuilder"/> to configure.</param>
        /// <returns>The <see cref="IApplicationBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public static IApplicationBuilder UseRequestResponseLogging(this IApplicationBuilder app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            return app.UseMiddleware<RequestResponseLoggingMiddleware>();
        }
    }
}
#endif