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

 
