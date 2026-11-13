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

## SQL connection pool metrics

With the OpenTelemetry provider, the server also exports the SqlClient connection pool counters from the `Microsoft.Data.SqlClient.EventSource` event source, using `OpenTelemetry.Instrumentation.EventCounters`. They are published on the `OpenTelemetry.Instrumentation.EventCounters` meter:

* Snapshot counters, such as `active-hard-connections`, `active-soft-connects`, `number-of-free-connections` and `number-of-active-connection-pools`, are gauges.
* Per-interval counters, such as `hard-connects`, `soft-connects` and `number-of-reclaimed-connections`, are cumulative counters.
* Instrument names are `ec.{event source}.{counter}`, limited to 63 characters by shortening the event source name. For example, `ec.Microsoft.Data.SqlClient.EventSource.active-hard-connections` fits, and `number-of-free-connections` becomes `ec.Microsoft.Data.SqlClient.EventSou.number-of-free-connections`.
* The values are totals across every connection pool in the process. SqlClient creates one pool per distinct connection string, and `SqlServer:MaxPoolSize` limits each pool separately.

Other hosts can export the same counters by adding the instrumentation to their own meter provider:

```csharp
meterProviderBuilder.AddEventCountersInstrumentation(options => options.AddEventSources("Microsoft.Data.SqlClient.EventSource"));
```

 
