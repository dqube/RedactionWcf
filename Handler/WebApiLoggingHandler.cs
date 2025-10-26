using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace RedactionWcf.Handler
{
    /// <summary>
    /// Web API Message Handler to capture request and response bodies
    /// This works where Response.Filter doesn't for Web API
    /// </summary>
    public class WebApiLoggingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Capture request body if present
            if (request.Content != null)
            {
                try
                {
                    var requestBody = await request.Content.ReadAsStringAsync();
                    if (HttpContext.Current != null && !string.IsNullOrEmpty(requestBody))
                    {
                        HttpContext.Current.Items["WebAPI_RequestBody"] = requestBody;
                        Debug.WriteLine($"[WebApiLoggingHandler] Captured request body: {requestBody.Length} bytes");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WebApiLoggingHandler] Error capturing request: {ex.Message}");
                }
            }

            // Call the next handler in the pipeline
            var response = await base.SendAsync(request, cancellationToken);

            // Capture response body
            if (response.Content != null)
            {
                try
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    if (HttpContext.Current != null && !string.IsNullOrEmpty(responseBody))
                    {
                        HttpContext.Current.Items["WebAPI_ResponseBody"] = responseBody;
                        Debug.WriteLine($"[WebApiLoggingHandler] Captured response body: {responseBody.Length} bytes");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WebApiLoggingHandler] Error capturing response: {ex.Message}");
                }
            }

            return response;
        }
    }
}
