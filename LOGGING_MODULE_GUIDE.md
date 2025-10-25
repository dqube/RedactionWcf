# Request/Response Logging Module Implementation Guide

## Overview
This document describes the implementation of the HTTP Module for logging requests and responses with correlation tracking capabilities for both WCF services and Web API controllers.

## Features
- ? Captures all HTTP requests and responses
- ? Extracts CorrelationId, ConsumerId, and UserId from headers
- ? Falls back to extracting IDs from request body if not in headers
- ? Works with both WCF services (.svc) and Web API controllers
- ? Logs requests, responses, and errors
- ? Redacts sensitive information (passwords, tokens, etc.)
- ? Includes request duration tracking
- ? Registered in Web.config for both Classic and Integrated pipeline modes

## Architecture

### HTTP Module: RequestResponseLoggingModule
**Location**: `Modules\RequestResponseLoggingModule.cs`

**Features**:
1. **BeginRequest**: Captures request details and extracts correlation information
2. **EndRequest**: Logs response details with duration
3. **OnError**: Logs any unhandled exceptions

### Correlation Headers
The module looks for these headers:
- `X-Correlation-Id`: Unique request identifier (auto-generated if not provided)
- `X-Consumer-Id`: Identifies the consuming application
- `X-User-Id`: Identifies the user making the request

### Request Body Fallback
If headers are not present, the module attempts to extract these values from the JSON request body:
```json
{
  "ConsumerId": "consumer-123",
  "UserId": "user-456",
  ...other fields
}
```

## Configuration

### Web.config Registration
The module is registered in two places for maximum compatibility:

```xml
<system.web>
  <httpModules>
    <add name="RequestResponseLoggingModule" type="RedactionWcf.Modules.RequestResponseLoggingModule, RedactionWcf" />
  </httpModules>
</system.web>

<system.webServer>
  <modules>
    <add name="RequestResponseLoggingModule" type="RedactionWcf.Modules.RequestResponseLoggingModule, RedactionWcf" preCondition="managedHandler" />
  </modules>
</system.webServer>
```

## Usage Examples

### 1. Testing with HTTP Headers
```http
POST http://localhost:63124/AppointmentService.svc/appointments
X-Correlation-Id: test-correlation-123
X-Consumer-Id: mobile-app
X-User-Id: user@example.com
Content-Type: application/json

{
  "Title": "Test Appointment",
  "PatientName": "John Doe",
  ...
}
```

### 2. Testing with Body Parameters
```http
POST http://localhost:63124/AppointmentService.svc/appointments
Content-Type: application/json

{
  "ConsumerId": "web-portal",
  "UserId": "admin@example.com",
  "Title": "Test Appointment",
  "PatientName": "Jane Smith",
  ...
}
```

### 3. Accessing Context in WCF Service
```csharp
public class AppointmentService : IAppointmentService
{
    public Appointment CreateAppointment(Appointment appointment)
    {
        // Access correlation information
        var context = HttpContext.Current.Items["RequestContext"];
        var correlationId = HttpContext.Current.Response.Headers["X-Correlation-Id"];
        
        // Your business logic here
        ...
    }
}
```

### 4. Accessing Context in Web API Controller
```csharp
public class AppointmentController : ApiController
{
    public IHttpActionResult Post([FromBody] Appointment appointment)
    {
        // Access correlation information
        var correlationId = HttpContext.Current.Response.Headers["X-Correlation-Id"];
        
        // Your business logic here
        ...
    }
}
```

## Log Output Format

### Request Log
```json
{
  "LogType": "Request",
  "Timestamp": "2024-01-15T10:30:00Z",
  "CorrelationId": "abc-123-def-456",
  "ConsumerId": "mobile-app",
  "UserId": "user@example.com",
  "Method": "POST",
  "Path": "/AppointmentService.svc/appointments",
  "QueryString": "",
  "ContentType": "application/json",
  "Headers": {
    "Content-Type": "application/json",
    "X-Correlation-Id": "abc-123-def-456"
  },
  "Body": "{\"Title\":\"Annual Checkup\",...}"
}
```

### Response Log
```json
{
  "LogType": "Response",
  "Timestamp": "2024-01-15T10:30:01Z",
  "CorrelationId": "abc-123-def-456",
  "ConsumerId": "mobile-app",
  "UserId": "user@example.com",
  "StatusCode": 200,
  "StatusDescription": "OK",
  "ContentType": "application/json",
  "Duration": 125.5,
  "Headers": {
    "Content-Type": "application/json",
    "X-Correlation-Id": "abc-123-def-456"
  },
  "Body": "{\"Id\":1,\"Title\":\"Annual Checkup\",...}"
}
```

### Error Log
```json
{
  "LogType": "Error",
  "Timestamp": "2024-01-15T10:30:00Z",
  "CorrelationId": "abc-123-def-456",
  "ConsumerId": "mobile-app",
  "UserId": "user@example.com",
  "Method": "POST",
  "Path": "/AppointmentService.svc/appointments",
  "ErrorMessage": "Title is required",
  "ErrorType": "WebFaultException",
  "StackTrace": "...",
  "InnerException": null
}
```

## Security Features

### Sensitive Data Redaction
The module automatically redacts sensitive fields:
- **Headers**: Authorization, Cookie, Set-Cookie, X-API-Key
- **Body Fields**: password, token, secret, apiKey (case-insensitive)

Example:
```json
{
  "username": "john@example.com",
  "password": "***REDACTED***",
  "apiKey": "***REDACTED***"
}
```

### Body Size Limits
- Request/response bodies larger than 10,000 characters are truncated
- Truncation marker added: `[TRUNCATED]`

## Troubleshooting

### Module Not Loading
1. Verify Web.config registration
2. Check assembly name matches: `RedactionWcf`
3. Ensure module class is public
4. Check Application Pool permissions

### Context Not Available
1. Verify module is registered before other handlers
2. Check that `aspNetCompatibilityEnabled="true"` for WCF services
3. Ensure requests go through ASP.NET pipeline

### Logs Not Appearing
1. Check Output window in Visual Studio (Debug view)
2. Verify Trace listeners are configured
3. Check event log for errors
4. Enable detailed tracing in Web.config:
```xml
<system.diagnostics>
  <trace autoflush="true">
    <listeners>
      <add name="textWriterTraceListener" type="System.Diagnostics.TextWriterTraceListener" initializeData="C:\Logs\trace.log" />
    </listeners>
  </trace>
</system.diagnostics>
```

## Testing

### Test Headers
Update your `AppointmentService.http` file to include correlation headers:

```http
### Test with Correlation Headers
POST {{serviceUrl}}/appointments
X-Correlation-Id: test-{{$randomUUID}}
X-Consumer-Id: http-test-client
X-User-Id: tester@example.com
Content-Type: application/json

{
  "Title": "Test with Headers",
  "PatientName": "Test Patient",
  "AppointmentDate": "2024-12-20T10:00:00",
  "Location": "Room 101",
  "DoctorName": "Dr. Test",
  "DurationMinutes": 30
}
```

### Test Body Parameters
```http
### Test with Body Parameters
POST {{serviceUrl}}/appointments
Content-Type: application/json

{
  "ConsumerId": "body-test-client",
  "UserId": "body-tester@example.com",
  "Title": "Test with Body",
  "PatientName": "Test Patient",
  "AppointmentDate": "2024-12-20T10:00:00",
  "Location": "Room 102",
  "DoctorName": "Dr. Test",
  "DurationMinutes": 30
}
```

## Performance Considerations

1. **Stream Reading**: Module uses efficient stream reading with position reset
2. **Memory Management**: Large bodies are truncated to prevent memory issues
3. **JSON Parsing**: Only attempts JSON parsing for application/json content
4. **Error Handling**: All operations wrapped in try-catch to prevent request failures

## Future Enhancements

- [ ] Support for async logging
- [ ] Database logging integration
- [ ] Configurable redaction rules
- [ ] Response body capture via stream wrapper
- [ ] Support for XML content type
- [ ] Integration with Application Insights / ELK Stack
- [ ] Request/Response compression handling

## Support

For issues or questions:
1. Check Visual Studio Output window for detailed logs
2. Review this documentation
3. Check Web.config for proper registration
4. Verify module compilation without errors
