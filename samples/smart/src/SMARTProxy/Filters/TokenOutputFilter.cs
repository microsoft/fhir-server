using Microsoft.AzureHealth.DataServices.Clients.Headers;
using Microsoft.AzureHealth.DataServices.Filters;
using Microsoft.AzureHealth.DataServices.Json;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SMARTProxy.Configuration;
using System.Text.RegularExpressions;

namespace SMARTProxy.Filters
{
    public class TokenOutputFilter : IOutputFilter
    {
        private readonly ILogger _logger;
        private readonly SMARTProxyConfig _configuration;

        public TokenOutputFilter(ILogger<TokenOutputFilter> logger, SMARTProxyConfig configuration)
        {
            _logger = logger;
            _configuration = configuration;
            id = Guid.NewGuid().ToString();
        }

        public event EventHandler<FilterErrorEventArgs> OnFilterError;

        public string Name => nameof(AuthorizeInputFilter);

        public StatusType ExecutionStatusType => StatusType.Normal;

        private readonly string id;
        string IFilter.Id => id;

        public async Task<OperationContext> ExecuteAsync(OperationContext context)
        {
            // Only execute for token request
            if (!context.Request.RequestUri.LocalPath.Contains("token"))
            {
                return context;
            }
            _logger?.LogInformation("Entered {Name}", Name);

            JObject tokenResponse = JObject.Parse(context.ContentString);

            // TODO: Check for fhirUser in id_token

            // Replace scopes from fully qualified AD scopes to SMART scopes
            if (!tokenResponse["scope"]!.IsNullOrEmpty())
            {
                var ns = tokenResponse["scope"]!.ToString();
                ns = Regex.Replace(ns, @"api://[A-Za-z0-9\-]+/", "");
                ns = ns.Replace("patient.", "patient/");
                ns = ns.Replace("encounter.", "encounter/");
                ns = ns.Replace("user.", "user/");
                ns = ns.Replace("system.", "system/");
                ns = ns.Replace("launch.", "launch/");
                if (!ns.Contains("offline_access")) ns += " offline_access";
                if (!ns.Contains("openid")) ns += " openid";
                tokenResponse["scope"] = ns;
            }

            context.ContentString = tokenResponse.ToString();

            //context.Headers.Add(new HeaderNameValuePair("Content-Type", "application/json", CustomHeaderType.ResponseStatic));
            context.Headers.Add(new HeaderNameValuePair("Cache-Control", "no-store", CustomHeaderType.ResponseStatic));
            context.Headers.Add(new HeaderNameValuePair("Pragma", "no-cache", CustomHeaderType.ResponseStatic));

            return context;
        }
    }
}
