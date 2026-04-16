# Enabling telemetry logging with Application Insights 

FHIR server enables telemetry logging to your Application Insights resource by updating the Telemetry section in appsettings.json.  
1. Application Insights logging package with the Application Insights instrumentation key. 
1. Application Insights logging package with the Application Insights connection string. 

Properties in Telemetry section in appsettings.json
* Provider : Telemetry provider is “ApplicationInsights” or “None” for disabling telemetry logging. 
* InstrumentationKey : The instrumentation key of your Application Insights resource. 
* ConnectionString : The connection string of your Application Insights resource. 

**Note**: 
1. Technical support for instrumentation key-based logging in Application Insights will end in March 2025. 
2. When the instrumentation key and connection string are provided in appsettings.json for ApplicationInsights provider, the instrumentation key will be used. 

Next Steps: OpenTelemetry provider is still experimental-mode, not recommended for production use. 
To use OTEL, set the provider value to “OpenTelemetry” in appsettings.json. Incase the connection string is used for OpenTelemetry provider, the instrumentation key will be ignored. 

## SQL resource metrics watchdog

The SQL provider can emit periodic Azure SQL Database resource metrics through the existing `FhirServer` meter by enabling the SQL metrics watchdog in `FhirServer:Watchdog`.

Example:

```json
"FhirServer": {
  "Watchdog": {
    "SqlMetrics": {
      "Enabled": true,
      "PeriodSeconds": 60
    }
  }
}
```

When enabled, the SQL provider records the following histogram metrics once per interval:

- `Sql.Database.CpuPercent`
- `Sql.Database.DataIoPercent`
- `Sql.Database.LogIoPercent`
- `Sql.Database.MemoryPercent`
- `Sql.Database.WorkersPercent`
- `Sql.Database.SessionsPercent`

The watchdog uses `sys.dm_db_resource_stats`, so it is intended for Azure SQL Database and requires `VIEW DATABASE STATE`.

 
