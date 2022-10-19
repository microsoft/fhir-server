// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.AzureHealth.DataServices.Clients.Headers;
using Microsoft.AzureHealth.DataServices.Filters;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using SMARTProxy.Configuration;
using SMARTProxy.Extensions;
using SMARTProxy.Models;

namespace SMARTProxy.Filters
{
    public sealed class TokenInputFilter : IInputFilter
    {
        private readonly ILogger _logger;
        private readonly SMARTProxyConfig _configuration;
        private readonly string _id;

        public TokenInputFilter(ILogger<TokenInputFilter> logger, SMARTProxyConfig configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _id = Guid.NewGuid().ToString();
        }

#pragma warning disable CS0067 // Needed to implement interface.
        public event EventHandler<FilterErrorEventArgs>? OnFilterError;
#pragma warning restore CS0067 // Needed to implement interface.

        public string Name => nameof(TokenInputFilter);

        public StatusType ExecutionStatusType => StatusType.Normal;

        string IFilter.Id => _id;

        public async Task<OperationContext> ExecuteAsync(OperationContext context)
        {
            // Only execute for token request
            if (!context.Request.RequestUri!.LocalPath.Contains("token", StringComparison.InvariantCultureIgnoreCase))
            {
                return context;
            }

            _logger?.LogInformation("Entered {Name}", Name);

            if (!context.Request.Content!.Headers.GetValues("Content-Type").Single().Contains("application/x-www-form-urlencoded", StringComparison.CurrentCultureIgnoreCase))
            {
                context.IsFatal = true;
                context.ContentString = "Content Type must be application/x-www-form-urlencoded.";
                context.StatusCode = HttpStatusCode.BadRequest;
                _logger.LogTrace("Content Type must be application/x-www-form-urlencoded.");
                return context;
            }

            // Parse the request body
            TokenContext? tokenContext = null;
            try
            {
                tokenContext = ParseTokenContext(context, _logger!, _configuration);
                tokenContext.Validate();
            }
            catch (Exception ex)
            {
                context.IsFatal = true;
                context.ContentString = "Token request invalid.";
                context.StatusCode = HttpStatusCode.BadRequest;
                _logger.LogError(ex, "Token request invalid. {TokenContext}", tokenContext?.ToLogString() ?? await context.Request.Content.ReadAsStringAsync());
                return context;
            }

            // Setup new http client for token request
            string tokenEndpoint = "https://login.microsoftonline.com/";
            string tokenPath = $"{_configuration.TenantId}/oauth2/v2.0/token";

            context.UpdateRequestUri(context.Request.Method, tokenEndpoint, tokenPath);
            context.Request.Content = tokenContext.ToFormUrlEncodedContent();

            // TODO - change to "NeedsCoors" or something similar.
            if (tokenContext.GetType() == typeof(PublicClientTokenContext))
            {
                context.Headers.Add(new HeaderNameValuePair("Origin", "http://localhost", CustomHeaderType.RequestStatic));
            }

            return context;
        }

        // parse async token context
        private static TokenContext ParseTokenContext(OperationContext context, ILogger logger, SMARTProxyConfig _configuration)
        {
            var contentStr = context.ContentString;
            var req = context.Request;

            if (!req.Content!.Headers.GetValues("Content-Type").Single().Contains("application/x-www-form-urlencoded", StringComparison.CurrentCultureIgnoreCase))
            {
                throw new ArgumentException("Content-Type must be application/x-www-form-urlencoded for requests to the token endpoint.");
            }

            // Inferno 1 Standalone Patient App depends on symetric confidential client
            // We have no choice but to provide client secret on the tests and this forces the basic auth header in the test.
            // https://github.com/inferno-framework/smart-app-launch-test-kit/blob/b7fbba193f43b65fd00568e18591a8518210f2d0/lib/smart_app_launch/token_exchange_test.rb#L51

            var reqAuth = req.Headers!.Authorization;

            // TODO - this may need refactoring and needs better tests / error handling
            if (reqAuth?.Scheme == "Basic" && reqAuth?.Parameter is not null)
            {
                logger?.LogTrace("Request is using basic auth via header.");
                var authParameterDecoded = reqAuth!.Parameter!.DecodeBase64().Split(":");

                contentStr += $"&client_id={authParameterDecoded[0]}";
                contentStr += $"&client_secret={authParameterDecoded[1]}";
            }

            return TokenContext.FromFormUrlEncodedContent(contentStr, _configuration.Audience!);
        }
    }
}
