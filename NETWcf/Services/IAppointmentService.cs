using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Web;
using RedactionWcf.Models;

namespace RedactionWcf.Services
{
    /// <summary>
    /// WCF REST Service Contract for Appointment operations
    /// </summary>
    [ServiceContract]
    public interface IAppointmentService
    {
        [OperationContract]
        [WebGet(UriTemplate = "/appointments", ResponseFormat = WebMessageFormat.Json)]
        List<Appointment> GetAllAppointments();

        [OperationContract]
        [WebGet(UriTemplate = "/appointments/{id}", ResponseFormat = WebMessageFormat.Json)]
        Appointment GetAppointment(string id);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "/appointments")]
        Appointment CreateAppointment(Appointment appointment);
    }
}
