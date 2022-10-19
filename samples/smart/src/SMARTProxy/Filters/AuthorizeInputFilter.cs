// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using Microsoft.AzureHealth.DataServices.Clients.Headers;
using Microsoft.AzureHealth.DataServices.Filters;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using SMARTProxy.Configuration;
using SMARTProxy.Extensions;
using SMARTProxy.Models;

namespace SMARTProxy.Filters
{
    public sealed class AuthorizeInputFilter : IInputFilter
    {
        private readonly ILogger _logger;
        private readonly SMARTProxyConfig _configuration;
        private readonly string _id;

        public AuthorizeInputFilter(ILogger<AuthorizeInputFilter> logger, SMARTProxyConfig configuration)
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
            // Only execute for authorize request
            if (!context.Request!.RequestUri!.LocalPath.Contains("authorize", StringComparison.CurrentCultureIgnoreCase))
            {
                return context;
            }

            _logger?.LogInformation("Entered {Name}", Name);

            // Get and parse SMART launch params from the request
            var launchContext = await ParseLaunchContext(context.Request);

            // Verify required SMART launch params are not null
            if (!launchContext.ValidateLaunchContext())
            {
                context.IsFatal = true;
                context.ContentString = "Missing required SMART Launch parameters";
                context.StatusCode = HttpStatusCode.BadRequest;
                _logger.LogInformation("Bad request - required launch parameters missing. {LaunchContext}", JsonSerializer.Serialize(launchContext));
                return context;
            }

            // Verify response_type is "code"
            if (!launchContext.ValidateResponseType())
            {
                context.IsFatal = true;
                context.ContentString = "Invalid response_type. Only 'code' is supported";
                context.StatusCode = HttpStatusCode.BadRequest;
                _logger.LogInformation("Invalid response type  {ResponseType}.", launchContext.ResponseType);
                return context;
            }

            // Explicitly set audience and scopes to the application registration that holds the SMART scopes.
            var scopes = launchContext.Scope?.ParseScope(_configuration.Audience!);
            launchContext.Aud = _configuration.Audience!;

            // Build the aad authorize url
            var authUrl = "https://login.microsoftonline.com";
            var authPath = $"{_configuration.TenantId}/oauth2/v2.0/authorize";
            var redirectUrl = $"{authUrl}/{authPath}";
            var redirect_querystring = launchContext.ToRedirectQueryString(scopes!);
            var newRedirectUrl = $"{redirectUrl}?{redirect_querystring}";

            context.StatusCode = HttpStatusCode.Redirect;
            context.Headers.Add(new HeaderNameValuePair("Location", newRedirectUrl, CustomHeaderType.ResponseStatic));
            context.Headers.Add(new HeaderNameValuePair("Origin", "http://localhost", CustomHeaderType.ResponseStatic));
            context.Request.RequestUri = new Uri(newRedirectUrl);

            await Task.CompletedTask;

            return context;
        }

        private static async Task<LaunchContext> ParseLaunchContext(HttpRequestMessage req)
        {
            LaunchContext launchContext;

            if (req.Method == HttpMethod.Post)
            {
                if (req.Content!.Headers.GetValues("Content-Type").Single().Contains("application/x-www-form-urlencoded", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (req.Content is null)
                    {
                        throw new ArgumentException("Body must contain data");
                    }

                    launchContext = new LaunchContextBuilder()
                        .FromNameValueCollection(await req.Content.ReadAsFormDataAsync())
                        .Build();
                }
                else
                {
                    throw new ArgumentException("Unsupported Content-Type");
                }
            }
            else if (req.Method == HttpMethod.Get)
            {
                launchContext = new LaunchContextBuilder()
                    .FromNameValueCollection(req.RequestUri.ParseQueryString())
                    .Build();
            }
            else
            {
                throw new ArgumentException("Unsupported HTTP method");
            }

            return launchContext;
        }
    }
}
