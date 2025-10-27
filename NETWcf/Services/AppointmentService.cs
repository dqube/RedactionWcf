using mylogging.observability.Framework;
using RedactionWcf.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Web;
using System.Diagnostics;
using System.Web;

namespace RedactionWcf.Services
{
    /// <summary>
    /// Implementation of the Appointment WCF REST Service
    /// </summary>
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
    [AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Allowed)]
    public class AppointmentService : IAppointmentService
    {
        // In-memory storage for demonstration purposes
        private static List<Appointment> appointments = new List<Appointment>
        {
            new Appointment
            {
                Id = 1,
                Title = "Annual Checkup",
                Description = "Routine physical examination",
                AppointmentDate = DateTime.Now.AddDays(7),
                Location = "Room 101",
                PatientName = "John Doe",
                DoctorName = "Dr. Smith",
                DurationMinutes = 30
            },
            new Appointment
            {
                Id = 2,
                Title = "Dental Cleaning",
                Description = "Regular teeth cleaning",
                AppointmentDate = DateTime.Now.AddDays(14),
                Location = "Room 205",
                PatientName = "Jane Smith",
                DoctorName = "Dr. Johnson",
                DurationMinutes = 45
            }
        };

        /// <summary>
        /// Get all appointments
        /// </summary>
        public List<Appointment> GetAllAppointments()
        {
            return appointments;
        }

        /// <summary>
        /// Get a specific appointment by ID
        /// </summary>
        public Appointment GetAppointment(string id)
        {
            if (int.TryParse(id, out int appointmentId))
            {
                return appointments.FirstOrDefault(a => a.Id == appointmentId);
            }
            return null;
        }

        /// <summary>
        /// Create a new appointment
        /// </summary>
        public Appointment CreateAppointment(Appointment appointment)
        {
            // Method 1: Access via Activity.Current (now works!)
            var activity = System.Diagnostics.Activity.Current;

            // Method 2: Access via HttpContext (alternative)
            var activityFromContext = HttpContext.Current?.Items[$"{TelemetryConfiguration.Options?.ServiceName ?? "WcfTelemetry"}.Activity"] as Activity;

            // ContextProvider.CorrelationId works independently
            string correlationId = ContextProvider.CorrelationId;

            if (appointment == null)
            {
                throw new WebFaultException<string>("Appointment data is required", System.Net.HttpStatusCode.BadRequest);
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(appointment.Title))
            {
                throw new WebFaultException<string>("Title is required", System.Net.HttpStatusCode.BadRequest);
            }

            if (string.IsNullOrWhiteSpace(appointment.PatientName))
            {
                throw new WebFaultException<string>("Patient name is required", System.Net.HttpStatusCode.BadRequest);
            }

            // Auto-generate ID
            appointment.Id = appointments.Any() ? appointments.Max(a => a.Id) + 1 : 1;

            // Add to collection
            appointments.Add(appointment);

            return appointment;
        }
    }
}
