// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.AzureHealth.DataServices.Clients.Headers;
using Microsoft.AzureHealth.DataServices.Filters;
using Microsoft.AzureHealth.DataServices.Json;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SMARTProxy.Configuration;

namespace SMARTProxy.Filters
{
    public sealed class TokenOutputFilter : IOutputFilter
    {
        private readonly ILogger _logger;
        private readonly SMARTProxyConfig _configuration;
        private readonly string _id;

        public TokenOutputFilter(ILogger<TokenOutputFilter> logger, SMARTProxyConfig configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _id = Guid.NewGuid().ToString();
        }

#pragma warning disable CS0067 // Needed to implement interface.
        public event EventHandler<FilterErrorEventArgs>? OnFilterError;
#pragma warning restore CS0067 // Needed to implement interface.

        public string Name => nameof(AuthorizeInputFilter);

        public StatusType ExecutionStatusType => StatusType.Normal;

        string IFilter.Id => _id;

        public async Task<OperationContext> ExecuteAsync(OperationContext context)
        {
            // Only execute for token request
            if (context.Request.RequestUri is not null || !context.Request.RequestUri!.LocalPath.Contains("token", StringComparison.CurrentCultureIgnoreCase))
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
                ns = Regex.Replace(ns, @"api://[A-Za-z0-9\-]+/", string.Empty);
                ns = ns.Replace("patient.", "patient/", StringComparison.CurrentCulture);
                ns = ns.Replace("encounter.", "encounter/", StringComparison.CurrentCulture);
                ns = ns.Replace("user.", "user/", StringComparison.CurrentCulture);
                ns = ns.Replace("system.", "system/", StringComparison.CurrentCulture);
                ns = ns.Replace("launch.", "launch/", StringComparison.CurrentCulture);

                if (!ns.Contains("offline_access", StringComparison.CurrentCulture))
                {
                    ns += " offline_access";
                }

                if (!ns.Contains("openid", StringComparison.CurrentCulture))
                {
                    ns += " openid";
                }

                tokenResponse["scope"] = ns;
            }

            context.ContentString = tokenResponse.ToString();

            // context.Headers.Add(new HeaderNameValuePair("Content-Type", "application/json", CustomHeaderType.ResponseStatic));

            context.Headers.Add(new HeaderNameValuePair("Cache-Control", "no-store", CustomHeaderType.ResponseStatic));
            context.Headers.Add(new HeaderNameValuePair("Pragma", "no-cache", CustomHeaderType.ResponseStatic));

            await Task.CompletedTask;

            return context;
        }
    }
}
