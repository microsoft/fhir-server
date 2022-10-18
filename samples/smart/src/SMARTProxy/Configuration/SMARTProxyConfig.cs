namespace SMARTProxy.Configuration
{
    public class SMARTProxyConfig
    {
        public string? FhirServerUrl { get; set; }
        public string? AppInsightsConnectionString { get; set; }
        public string? AppInsightsInstrumentationKey { get; set; }

        public string? TenantId { get; set; }

        public void Validate()
        {
            if (string.IsNullOrEmpty(TenantId))
            {
                throw new ArgumentException("TenantId must be configured for this application.");
            }
        }
    }
}