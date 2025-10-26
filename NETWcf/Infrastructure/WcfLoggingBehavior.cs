using System;
using System.IO;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Channels;
using System.ServiceModel.Configuration;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;

namespace RedactionWcf.Infrastructure
{
    public class WcfLoggingInspector : IDispatchMessageInspector
    {
        public object AfterReceiveRequest(ref Message request, IClientChannel channel, InstanceContext instanceContext)
        {
            try
            {
                if (HttpContext.Current != null && request != null)
                {
                    var buffer = request.CreateBufferedCopy(Int32.MaxValue);
                    request = buffer.CreateMessage();
                    var copy = buffer.CreateMessage();

                    using (var ms = new MemoryStream())
                    using (var writer = XmlWriter.Create(ms))
                    {
                        copy.WriteMessage(writer);
                        writer.Flush();
                        ms.Position = 0;
                        using (var reader = new StreamReader(ms, Encoding.UTF8))
                        {
                            var body = reader.ReadToEnd();

                            // Extract JSON content from the message if it's wrapped
                            var extractedBody = ExtractJsonFromMessage(body);

                            HttpContext.Current.Items["WCF_RequestBody"] = extractedBody ?? body;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Suppress errors to prevent disrupting the request pipeline
            }
            return null;
        }

        public void BeforeSendReply(ref Message reply, object correlationState)
        {
            try
            {
                if (HttpContext.Current != null && reply != null)
                {
                    var buffer = reply.CreateBufferedCopy(Int32.MaxValue);
                    reply = buffer.CreateMessage();
                    var copy = buffer.CreateMessage();

                    using (var ms = new MemoryStream())
                    using (var writer = XmlWriter.Create(ms))
                    {
                        copy.WriteMessage(writer);
                        writer.Flush();
                        ms.Position = 0;
                        using (var reader = new StreamReader(ms, Encoding.UTF8))
                        {
                            var body = reader.ReadToEnd();

                            // Extract JSON content from the message if it's wrapped
                            var extractedBody = ExtractJsonFromMessage(body);

                            HttpContext.Current.Items["WCF_ResponseBody"] = extractedBody ?? body;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Suppress errors to prevent disrupting the response pipeline
            }
        }

        /// <summary>
        /// Extracts JSON content from WCF REST XML message wrapper
        /// </summary>
        private string ExtractJsonFromMessage(string xmlMessage)
        {
            try
            {
                if (string.IsNullOrEmpty(xmlMessage))
                    return null;

                // For WebHttpBinding with JSON, the actual JSON is inside a CDATA or text node
                // Pattern 1: Look for JSON object pattern {...}
                var jsonMatch = Regex.Match(xmlMessage, @"\{[\s\S]*\}", RegexOptions.Multiline);
                if (jsonMatch.Success)
                {
                    return jsonMatch.Value;
                }

                // Pattern 2: Look for JSON array pattern [...]
                var arrayMatch = Regex.Match(xmlMessage, @"\[[\s\S]*\]", RegexOptions.Multiline);
                if (arrayMatch.Success)
                {
                    return arrayMatch.Value;
                }

                // If no JSON found, return null to use the original XML
                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
    }

    public class WcfLoggingBehavior : IEndpointBehavior
    {
        public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters) { }
        public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime) { }
        public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher)
        {
            endpointDispatcher.DispatchRuntime.MessageInspectors.Add(new WcfLoggingInspector());
        }
        public void Validate(ServiceEndpoint endpoint) { }
    }

    public class WcfLoggingBehaviorExtension : BehaviorExtensionElement
    {
        public override Type BehaviorType => typeof(WcfLoggingBehavior);
        protected override object CreateBehavior() => new WcfLoggingBehavior();
    }

    /// <summary>
    /// Custom ServiceHostFactory that automatically adds logging behavior to all WCF endpoints
    /// </summary>
    public class LoggingWebServiceHostFactory : WebServiceHostFactory
    {
        protected override ServiceHost CreateServiceHost(Type serviceType, Uri[] baseAddresses)
        {
            var host = base.CreateServiceHost(serviceType, baseAddresses);

            // Add the logging behavior to all endpoints when the host opens
            host.Opening += (sender, args) =>
            {
                foreach (var endpoint in host.Description.Endpoints)
                {
                    endpoint.EndpointBehaviors.Add(new WcfLoggingBehavior());
                }
            };

            return host;
        }
    }
}
