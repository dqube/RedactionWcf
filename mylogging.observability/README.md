# MyLogging.Observability

A cross-platform logging and observability library that works seamlessly with both **.NET 8+** and **.NET Framework 4.8+**.

## Features

? **Multi-targeting Support**
- Targets both `net8.0` and `net48`
- Single codebase works across both platforms
- Platform-specific optimizations where needed

? **Request/Response Logging**
- Structured logging for HTTP requests and responses
- Correlation ID tracking
- Consumer and User ID tracking
- Execution time metrics

? **Error Logging**
- Detailed error information with stack traces
- Context preservation (request details, correlation IDs)
- Inner exception tracking

? **Metrics & Tracing**
- Custom metric logging
- Trace logging with properties
- Integration with System.Diagnostics

? **Multiple Output Options**
- ILogger integration (Microsoft.Extensions.Logging)
- Debug output (System.Diagnostics.Debug)
- Trace output (System.Diagnostics.Trace)

## Installation

### From Source
```bash
dotnet build mylogging.observability\mylogging.observability.csproj
```

### From NuGet (if published)
```bash
dotnet add package MyLogging.Observability
```

## Usage

### 1. Basic Setup

#### For .NET 8+ (ASP.NET Core)
```csharp
using Microsoft.Extensions.Logging;
using MyLogging.Observability;

// In your service configuration
services.AddSingleton<IObservabilityLogger>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ObservabilityLogger>>();
    var options = new ObservabilityOptions
    {
        PrettyPrintJson = true,
        EnableDebugOutput = true,
        EnableTraceOutput = true
    };
    return new ObservabilityLogger(logger, options);
});
```

#### For .NET Framework 4.8
```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using MyLogging.Observability;

// Create logger factory
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddDebug();
    builder.AddConsole();
});

var logger = loggerFactory.CreateLogger<ObservabilityLogger>();
var observabilityLogger = new ObservabilityLogger(logger);
```

### 2. Logging Request/Response

```csharp
var entry = new RequestResponseLogEntry
{
    CorrelationId = "abc-123",
    ConsumerId = "mobile-app",
    UserId = "user@example.com",
    ClassName = "ProductController",
    OperationName = "GetProduct",
    ExecutionTimeMs = 125.5,
    Request = new RequestDetails
    {
        Timestamp = DateTime.UtcNow,
        Method = "GET",
        Path = "/api/product/123",
        QueryString = "?filter=active",
        ContentType = "application/json",
        Headers = new Dictionary<string, string>
        {
            ["Accept"] = "application/json",
            ["X-Correlation-Id"] = "abc-123"
        },
        Body = ""
    },
    Response = new ResponseDetails
    {
        Timestamp = DateTime.UtcNow,
        StatusCode = 200,
        ContentType = "application/json",
        Headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json"
        },
        Body = "{\"id\":123,\"name\":\"Product A\"}"
    }
};

observabilityLogger.LogRequestResponse(entry);
```

### 3. Logging Errors

```csharp
var errorEntry = new ErrorLogEntry
{
    CorrelationId = "xyz-789",
    ConsumerId = "web-portal",
    UserId = "admin@example.com",
    ExecutionTimeMs = 50.2,
    ErrorMessage = "Product not found",
    ErrorType = "NotFoundException",
    StackTrace = exception.StackTrace,
    InnerException = exception.InnerException?.Message,
    Request = new RequestDetails
    {
        Timestamp = DateTime.UtcNow,
        Method = "GET",
        Path = "/api/product/999",
        QueryString = "",
        ContentType = null,
        Headers = new Dictionary<string, string>(),
        Body = ""
    }
};

observabilityLogger.LogError(errorEntry);
```

### 4. Logging Metrics

```csharp
// Simple metric
observabilityLogger.LogMetric("api_response_time", 125.5);

// Metric with properties
var properties = new Dictionary<string, object>
{
    ["endpoint"] = "/api/product",
    ["method"] = "GET",
    ["status_code"] = 200
};

observabilityLogger.LogMetric("api_call_count", 1, properties);
```

### 5. Logging Traces

```csharp
// Simple trace
observabilityLogger.LogTrace("User logged in successfully");

// Trace with properties
var traceProperties = new Dictionary<string, object>
{
    ["userId"] = "user@example.com",
    ["ipAddress"] = "192.168.1.100",
    ["userAgent"] = "Mozilla/5.0..."
};

observabilityLogger.LogTrace("User authentication attempt", traceProperties);
```

## Configuration Options

```csharp
var options = new ObservabilityOptions
{
    // Pretty print JSON output (default: true)
    PrettyPrintJson = true,
    
    // Enable Debug.WriteLine output (default: true)
    EnableDebugOutput = true,
    
    // Enable Trace output (default: true)
    EnableTraceOutput = true,
    
    // Maximum body size to log in bytes (default: 10MB)
    MaxBodySize = 10485760
};
```

## Integration Examples

### Integration with .NET Core Middleware

```csharp
// In RequestResponseLoggingMiddleware.cs
public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IObservabilityLogger _observabilityLogger;

    public RequestResponseLoggingMiddleware(
        RequestDelegate next,
        IObservabilityLogger observabilityLogger)
    {
        _next = next;
        _observabilityLogger = observabilityLogger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        
        // Capture request
        var requestBody = await ReadRequestBodyAsync(context.Request);
        
        // Process request
        await _next(context);
        
        stopwatch.Stop();
        
        // Log using observability logger
        var entry = new RequestResponseLogEntry
        {
            CorrelationId = context.Request.Headers["X-Correlation-Id"],
            ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds,
            // ... populate other fields
        };
        
        _observabilityLogger.LogRequestResponse(entry);
    }
}
```

### Integration with .NET Framework HTTP Module

```csharp
// In RequestResponseLoggingModule.cs
public class RequestResponseLoggingModule : IHttpModule
{
    private static IObservabilityLogger _observabilityLogger;
    
    static RequestResponseLoggingModule()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        var logger = loggerFactory.CreateLogger<ObservabilityLogger>();
        _observabilityLogger = new ObservabilityLogger(logger);
    }
    
    private void OnEndRequest(object sender, EventArgs e)
    {
        var application = (HttpApplication)sender;
        var context = application.Context;
        
        // Build log entry
        var entry = new RequestResponseLogEntry
        {
            // ... populate fields from HttpContext
        };
        
        _observabilityLogger.LogRequestResponse(entry);
    }
}
```

## Log Output Format

### Request/Response Log
```json
{
  "LogType": "RequestResponse",
  "CorrelationId": "abc-123",
  "ConsumerId": "mobile-app",
  "UserId": "user@example.com",
  "ClassName": "ProductController",
  "OperationName": "GetProduct",
  "ExecutionTimeMs": 125.5,
  "Request": {
    "Timestamp": "2024-01-15T10:30:00Z",
    "Method": "GET",
    "Path": "/api/product/123",
    "QueryString": "?filter=active",
    "ContentType": "application/json",
    "Headers": {
      "Accept": "application/json"
    },
    "Body": ""
  },
  "Response": {
    "Timestamp": "2024-01-15T10:30:01Z",
    "StatusCode": 200,
    "ContentType": "application/json",
    "Headers": {
      "Content-Type": "application/json"
    },
    "Body": "{\"id\":123,\"name\":\"Product A\"}"
  }
}
```

### Error Log
```json
{
  "LogType": "Error",
  "CorrelationId": "xyz-789",
  "ConsumerId": "web-portal",
  "UserId": "admin@example.com",
  "ExecutionTimeMs": 50.2,
  "Request": {
    "Timestamp": "2024-01-15T10:30:00Z",
    "Method": "GET",
    "Path": "/api/product/999",
    "QueryString": "",
    "ContentType": null,
    "Headers": {},
    "Body": ""
  },
  "Error": {
    "Timestamp": "2024-01-15T10:30:00Z",
    "ErrorMessage": "Product not found",
    "ErrorType": "NotFoundException",
    "StackTrace": "...",
    "InnerException": null
  }
}
```

### Metric Log
```json
{
  "LogType": "Metric",
  "MetricName": "api_response_time",
  "Value": 125.5,
  "Timestamp": "2024-01-15T10:30:00Z",
  "endpoint": "/api/product",
  "method": "GET",
  "status_code": 200
}
```

## Platform Differences

The library handles platform differences internally:

| Feature | .NET 8+ | .NET Framework 4.8 |
|---------|---------|-------------------|
| Microsoft.Extensions.Logging | Built-in | Via NuGet package |
| System.Text.Json | Built-in | Via NuGet package |
| Nullable reference types | Supported | Supported |
| C# 12 features | Supported | Supported |

## Building & Testing

### Build for all targets
```bash
dotnet build mylogging.observability\mylogging.observability.csproj
```

### Build for specific target
```bash
# .NET 8
dotnet build mylogging.observability\mylogging.observability.csproj -f net8.0

# .NET Framework 4.8
dotnet build mylogging.observability\mylogging.observability.csproj -f net48
```

### Run tests
```bash
dotnet test
```

## Dependencies

### .NET 8+
- System.Text.Json (8.0.5)
- Microsoft.Extensions.Logging.Abstractions (included in framework)

### .NET Framework 4.8
- System.Text.Json (8.0.5)
- Microsoft.Extensions.Logging.Abstractions (8.0.2)
- System.Diagnostics.DiagnosticSource (8.0.1)

## License

This is a demonstration project for learning purposes.

## Contributing

Contributions are welcome! Please ensure that any changes maintain compatibility with both .NET 8+ and .NET Framework 4.8+.

## Support

For issues or questions, please refer to the main workspace documentation.
