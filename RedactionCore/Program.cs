using mylogging.observability;
using mylogging.observability.Core;
using RedactionCore.Middleware;

namespace RedactionCore
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ✅ Read Observability configuration from appsettings.json
            var observabilityOptions = new ObservabilityOptions();
            builder.Configuration.GetSection("Observability").Bind(observabilityOptions);

            // ✅ Add Observability with Splunk exporter and redaction from configuration
               builder.Services.AddObservability(observabilityOptions);
            

            // Add services to the container.
            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // ✅ Log startup information
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("==============================================");
            logger.LogInformation("? Observability Configuration Loaded");
            logger.LogInformation("  Service: {ServiceName} v{ServiceVersion}", 
                observabilityOptions.ServiceName, observabilityOptions.ServiceVersion);
            logger.LogInformation("  Namespace: {Namespace}", observabilityOptions.ServiceNamespace);
            logger.LogInformation("  Business Process: {BusinessProcess}", observabilityOptions.BusinessProcess);
            logger.LogInformation("  Redaction: {Enabled}", observabilityOptions.EnableRedaction ? "Enabled" : "Disabled");
            logger.LogInformation("  Splunk Exporter: {Url}", observabilityOptions.SplunkExporter?.Url ?? "Not configured");
            logger.LogInformation("  Request/Response Logging: {Enabled}", observabilityOptions.EnableRequestResponseLogging ? "Enabled" : "Disabled");
            logger.LogInformation("  Sensitive Keys Count: {Count}", observabilityOptions.Redaction?.SensitiveKeys?.Count ?? 0);
            logger.LogInformation("==============================================");

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            // Add Request/Response Logging Middleware
            app.UseMiddleware<RequestResponseLoggingMiddleware>();

            app.UseAuthorization();

            app.MapControllers();

            // In-memory storage for appointments
            var appointments = new List<Appointment>();
            int nextId = 1;

            // Minimal API endpoint
            app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
                .WithName("HealthCheck");

            // CRUD endpoints for appointments
            app.MapGet("/api/appointments/GetAllAppointments", () => Results.Ok(appointments))
                .WithName("GetAllAppointments");

            app.MapGet("/api/appointments/GetAppointmentById/{id}", (int id) =>
            {
                var appointment = appointments.FirstOrDefault(a => a.Id == id);
                return appointment is not null ? Results.Ok(appointment) : Results.NotFound();
            })
            .WithName("GetAppointmentById");

            app.MapPost("/api/appointments/CreateAppointment", (Appointment appointment) =>
            {
                appointment.Id = nextId++;
                appointments.Add(appointment);
                return Results.Created($"/api/appointments/{appointment.Id}", appointment);
            })
            .WithName("CreateAppointment");

            app.MapPut("/api/appointments/UpdateAppointment/{id}", (int id, Appointment updatedAppointment) =>
            {
                var appointment = appointments.FirstOrDefault(a => a.Id == id);
                if (appointment is null)
                    return Results.NotFound();

                appointment.Title = updatedAppointment.Title;
                appointment.Description = updatedAppointment.Description;
                appointment.StartTime = updatedAppointment.StartTime;
                appointment.EndTime = updatedAppointment.EndTime;
                appointment.Location = updatedAppointment.Location;
                appointment.AttendeeEmail = updatedAppointment.AttendeeEmail;

                return Results.Ok(appointment);
            })
            .WithName("UpdateAppointment");

            app.MapDelete("/api/appointments/DeleteAppointment/{id}", (int id) =>
            {
                var appointment = appointments.FirstOrDefault(a => a.Id == id);
                if (appointment is null)
                    return Results.NotFound();

                appointments.Remove(appointment);
                return Results.NoContent();
            })
            .WithName("DeleteAppointment");

            app.Run();
        }
    }
}
