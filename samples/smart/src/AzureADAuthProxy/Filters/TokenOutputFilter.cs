// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Azure.Core;
using Azure.Identity;
using AzureADAuthProxy.Configuration;
using Microsoft.AzureHealth.DataServices.Clients.Headers;
using Microsoft.AzureHealth.DataServices.Filters;
using Microsoft.AzureHealth.DataServices.Json;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace AzureADAuthProxy.Filters
{
    public sealed class TokenOutputFilter : IOutputFilter
    {
        private readonly ILogger _logger;
        private readonly AzureADProxyConfig _configuration;
        private readonly string _id;

        public TokenOutputFilter(ILogger<TokenOutputFilter> logger, AzureADProxyConfig configuration)
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
            if (!context.Request.RequestUri!.LocalPath.Contains("token", StringComparison.CurrentCultureIgnoreCase))
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
                ns = ns.Replace(_configuration.Audience!, string.Empty, StringComparison.InvariantCulture);
                ns = ns.Replace("patient.", "patient/", StringComparison.InvariantCulture);
                ns = ns.Replace("encounter.", "encounter/", StringComparison.InvariantCulture);
                ns = ns.Replace("user.", "user/", StringComparison.InvariantCulture);
                ns = ns.Replace("system.", "system/", StringComparison.InvariantCulture);
                ns = ns.Replace("launch.", "launch/", StringComparison.InvariantCulture);

                // Why is this here?
                // if (!ns.Contains("offline_access", StringComparison.InvariantCulture))
                // {
                //     ns += " offline_access";
                // }

                // Why is this here?
                // if (!ns.Contains("openid", StringComparison.InvariantCulture))
                // {
                //     ns += " openid";
                // }

                tokenResponse["scope"] = ns;
            }
            else if (!tokenResponse["access_token"]!.IsNullOrEmpty())
            {
                var handler = new JwtSecurityTokenHandler();
                JwtSecurityToken access_token = (JwtSecurityToken)handler.ReadToken(tokenResponse["access_token"]!.ToString());

                var roles = access_token.Claims.Where(x => x.Type == "roles");
                if (roles is not null)
                {
                    tokenResponse["scope"] = string.Join(" ", roles.Select(x => x.Value));
                }
            }

            // BEGIN OVERRIDE - TODO remove as it's temp until we get fhirUser claim

            try
            {
                if (!context.IsFatal && context.StatusCode == (HttpStatusCode)200 && tokenResponse.ContainsKey("access_token"))
                {
                    _logger.LogInformation("Attempting to override the access token with MSI...");
                    var token = await GetOverrideAccessToken(_configuration.BackendFhirUrl!, _configuration.TenantId!);
                    tokenResponse["access_token"] = token.Token;
                    _logger.LogInformation("Access token overridden with {Token}", tokenResponse["access_token"]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not get token override. {Message}", ex.Message);
            }

            // END OVERRIDE

            context.ContentString = tokenResponse.ToString();

            // context.Headers.Add(new HeaderNameValuePair("Content-Type", "application/json", CustomHeaderType.ResponseStatic));

            context.Headers.Add(new HeaderNameValuePair("Cache-Control", "no-store", CustomHeaderType.ResponseStatic));
            context.Headers.Add(new HeaderNameValuePair("Pragma", "no-cache", CustomHeaderType.ResponseStatic));

            // Stop compiler warning
            await Task.CompletedTask;

            return context;
        }

        private static async ValueTask<AccessToken> GetOverrideAccessToken(string backendFhirUrl, string tenantId)
        {
            string[] scopes = new string[] { $"{backendFhirUrl}/.default" };
            TokenRequestContext tokenRequestContext = new TokenRequestContext(scopes: scopes, tenantId: tenantId);
            var credential = new DefaultAzureCredential(true);
            return await credential.GetTokenAsync(tokenRequestContext);
        }
    }
}
