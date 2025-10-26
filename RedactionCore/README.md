# RedactionCore - Request/Response Logging Middleware

## Overview
This .NET 8 Web API project demonstrates request/response logging with correlation tracking, similar to the .NET Framework HTTP Module pattern but implemented as ASP.NET Core middleware.

## Features

### ? Request/Response Logging Middleware
Located in `Middleware/RequestResponseLoggingMiddleware.cs`, this middleware:

- **Captures all API requests and responses** including:
  - Headers, body, query strings
  - HTTP method, path, status codes
  - Timestamps and execution duration
  
- **Correlation Tracking** via multiple header formats:
  - `X-Correlation-Id` (auto-generated if not provided)
  - `X-Consumer-Id` (identifies the consuming application)
  - `X-User-Id` (identifies the user making the request)
  - Supports alternative header names (case-insensitive)

- **Fallback ID Extraction** from request body:
  - Extracts IDs from JSON request bodies (root level or nested in `header`/`headers` object)
  - Extracts IDs from XML request bodies
  
- **Smart Request Filtering**:
  - Skips static files (.js, .css, images, etc.)
  - Skips Swagger UI and health check endpoints
  - Only logs actual API requests

- **Path Parsing**:
  - Extracts ClassName (controller) and OperationName (action) from the request path
  - Example: `/api/product/123` ? ClassName: "product", OperationName: "123"

## Project Structure

```
RedactionCore/
??? Controllers/
?   ??? ProductController.cs       # RESTful product management API
?   ??? WeatherForecastController.cs
??? Middleware/
?   ??? RequestResponseLoggingMiddleware.cs  # Main logging middleware
??? Program.cs                      # Application startup & middleware registration
??? Appointment.cs                  # Appointment model
??? Product.cs                      # Product model (in ProductController.cs)
??? RedactionCore.http             # HTTP test file with examples
```

## API Endpoints

### Product API (`/api/product`)
- `GET /api/product` - Get all products
- `GET /api/product/{id}` - Get product by ID
- `POST /api/product` - Create new product
- `PUT /api/product/{id}` - Update existing product
- `DELETE /api/product/{id}` - Delete product

### Appointment API (`/api/appointments`)
- `GET /api/appointments/GetAllAppointments` - Get all appointments
- `GET /api/appointments/GetAppointmentById/{id}` - Get appointment by ID
- `POST /api/appointments/CreateAppointment` - Create new appointment
- `PUT /api/appointments/UpdateAppointment/{id}` - Update existing appointment
- `DELETE /api/appointments/DeleteAppointment/{id}` - Delete appointment

### Utility Endpoints
- `GET /api/health` - Health check endpoint

## Usage Examples

### 1. Using Correlation Headers
```http
POST http://localhost:5107/api/product
Content-Type: application/json
X-Correlation-Id: test-12345
X-Consumer-Id: mobile-app
X-User-Id: user@example.com

{
  "name": "Laptop",
  "description": "High-performance laptop",
  "price": 1299.99,
  "category": "Electronics",
  "stockQuantity": 50
}
```

### 2. Using IDs in Request Body
```http
POST http://localhost:5107/api/product
Content-Type: application/json

{
  "correlationId": "body-correlation-123",
  "consumerId": "web-portal",
  "userId": "admin@example.com",
  "name": "Wireless Mouse",
  "description": "Ergonomic wireless mouse",
  "price": 29.99,
  "category": "Electronics",
  "stockQuantity": 200
}
```

### 3. Using Nested Headers in Body
```http
POST http://localhost:5107/api/product
Content-Type: application/json

{
  "header": {
    "correlationId": "nested-correlation-456",
    "consumerId": "mobile-app",
    "userId": "mobile-user@example.com"
  },
  "name": "Mechanical Keyboard",
  "description": "RGB mechanical gaming keyboard",
  "price": 149.99,
  "category": "Electronics",
  "stockQuantity": 75
}
```

## Log Output Format

### Request/Response Log
```json
{
  "LogType": "RequestResponse",
  "CorrelationId": "test-12345",
  "ConsumerId": "mobile-app",
  "UserId": "user@example.com",
  "ClassName": "product",
  "OperationName": null,
  "ExecutionTime": 125.5,
  "Request": {
    "Timestamp": "2024-01-15T10:30:00Z",
    "Method": "POST",
    "Path": "/api/product",
    "QueryString": "",
    "ContentType": "application/json",
    "Headers": {
      "Content-Type": "application/json",
      "X-Correlation-Id": "test-12345",
      "X-Consumer-Id": "mobile-app",
      "X-User-Id": "user@example.com"
    },
    "Body": "{\"name\":\"Laptop\",\"description\":\"High-performance laptop\",\"price\":1299.99,\"category\":\"Electronics\",\"stockQuantity\":50}"
  },
  "Response": {
    "Timestamp": "2024-01-15T10:30:01Z",
    "StatusCode": 201,
    "ContentType": "application/json",
    "Headers": {
      "Content-Type": "application/json",
      "X-Correlation-Id": "test-12345",
      "X-Consumer-Id": "mobile-app",
      "X-User-Id": "user@example.com"
    },
    "Body": "{\"id\":1,\"name\":\"Laptop\",\"description\":\"High-performance laptop\",\"price\":1299.99,\"category\":\"Electronics\",\"stockQuantity\":50}"
  }
}
```

### Error Log
```json
{
  "LogType": "Error",
  "CorrelationId": "test-12345",
  "ConsumerId": "mobile-app",
  "UserId": "user@example.com",
  "ExecutionTime": 50.2,
  "Request": {
    "Timestamp": "2024-01-15T10:30:00Z",
    "Method": "GET",
    "Path": "/api/product/9999",
    "QueryString": "",
    "ContentType": null,
    "Headers": {...},
    "Body": null
  },
  "Error": {
    "Timestamp": "2024-01-15T10:30:00Z",
    "Message": "Product with ID 9999 not found",
    "Type": "NotFoundException",
    "StackTrace": "...",
    "InnerException": null
  }
}
```

## Running the Application

1. **Start the application:**
   ```bash
   cd RedactionCore
   dotnet run
   ```

2. **Access Swagger UI:**
   ```
   http://localhost:5107/swagger
   ```

3. **Test with HTTP file:**
   - Open `RedactionCore.http` in Visual Studio
   - Click "Send Request" for any endpoint

4. **View Logs:**
   - Logs appear in the Visual Studio Output window (Debug pane)
   - Logs also go through ILogger infrastructure

## Middleware Configuration

The middleware is registered in `Program.cs`:

```csharp
// Add Request/Response Logging Middleware
app.UseMiddleware<RequestResponseLoggingMiddleware>();
```

**Important:** The middleware should be registered:
- **After** `app.UseHttpsRedirection()`
- **Before** `app.UseAuthorization()`
- **Before** `app.MapControllers()`

This ensures requests are logged after HTTPS redirection but before authorization checks.

## Comparison with .NET Framework Module

### .NET Framework (HTTP Module)
- Located in: `Modules/RequestResponseLoggingModule.cs`
- Registered in: `Web.config`
- Events: `BeginRequest`, `EndRequest`, `Error`
- Runs for: All HTTP requests (requires filtering)
- Context: Uses `HttpContext.Current`

### .NET Core (Middleware)
- Located in: `Middleware/RequestResponseLoggingMiddleware.cs`
- Registered in: `Program.cs`
- Method: `InvokeAsync(HttpContext context)`
- Runs for: All requests in the pipeline
- Context: Passed as parameter

### Key Differences
1. **Dependency Injection**: Middleware uses ILogger injected via constructor
2. **Async/Await**: Native async support in middleware
3. **Stream Handling**: Must enable request buffering and replace response stream
4. **Configuration**: Registered programmatically vs. Web.config

## Troubleshooting

### Logs Not Appearing
1. Check middleware is registered in correct order
2. Verify request is not being filtered (check `ShouldSkipLogging()`)
3. Check Visual Studio Output window is set to "Debug"

### Request Body Empty
1. Ensure request has `Content-Type` header
2. Check `Content-Length` is not exceeding 10MB limit
3. Verify `EnableBuffering()` is called

### Response Body Empty
1. Check response stream replacement logic
2. Verify response is being written to the replaced stream
3. Some endpoints may bypass the filter (like static files)

## Future Enhancements

- [ ] Add structured logging with Serilog
- [ ] Add database logging option
- [ ] Add request/response redaction for sensitive data
- [ ] Add compression handling
- [ ] Add configuration options (enable/disable, size limits, etc.)
- [ ] Add metrics and performance counters
- [ ] Integration with Application Insights or ELK Stack

## License

This is a demonstration project for learning purposes.
