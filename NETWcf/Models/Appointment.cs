using System;
using System.Runtime.Serialization;

namespace RedactionWcf.Models
{
    /// <summary>
    /// Represents an appointment in the system
    /// </summary>
    [DataContract]
    public class Appointment
    {
        [DataMember]
        public int Id { get; set; }

        [DataMember]
        public string Title { get; set; }

        [DataMember]
        public string Description { get; set; }

        [DataMember]
        public DateTime AppointmentDate { get; set; }

        [DataMember]
        public string Location { get; set; }

        [DataMember]
        public string PatientName { get; set; }

        [DataMember]
        public string DoctorName { get; set; }

        [DataMember]
        public int DurationMinutes { get; set; }
    }
}
