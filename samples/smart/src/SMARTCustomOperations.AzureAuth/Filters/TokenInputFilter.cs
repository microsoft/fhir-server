// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;
using System.Configuration;
using System.Net;
using Microsoft.AzureHealth.DataServices.Clients.Headers;
using Microsoft.AzureHealth.DataServices.Filters;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using SMARTCustomOperations.AzureAuth.Configuration;
using SMARTCustomOperations.AzureAuth.Extensions;
using SMARTCustomOperations.AzureAuth.Models;
using SMARTCustomOperations.AzureAuth.Services;

namespace SMARTCustomOperations.AzureAuth.Filters
{
    public sealed class TokenInputFilter : IInputFilter
    {
        private readonly ILogger _logger;
        private readonly AzureAuthOperationsConfig _configuration;
        private readonly string _id;
        private readonly IAsymmetricAuthorizationService _asymmetricAuthorizationService;

        public TokenInputFilter(ILogger<TokenInputFilter> logger, AzureAuthOperationsConfig configuration, IAsymmetricAuthorizationService asymmetricAuthorizationService)
        {
            _logger = logger;
            _configuration = configuration;
            _id = Guid.NewGuid().ToString();
            _asymmetricAuthorizationService = asymmetricAuthorizationService;
        }

        public event EventHandler<FilterErrorEventArgs>? OnFilterError;

        public string Name => nameof(AuthorizeInputFilter);

        public StatusType ExecutionStatusType => StatusType.Normal;

        public string Id => _id;

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
                FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: new ArgumentException("Content Type must be application/x-www-form-urlencoded"), code: HttpStatusCode.BadRequest);
                OnFilterError?.Invoke(this, error);
                return context.SetContextErrorBody(error, _configuration.Debug);
            }

            // Read the request body
            TokenContext? tokenContext = null;
            NameValueCollection requestData = await context.Request.Content.ReadAsFormDataAsync();

            // Parse the request body
            try
            {
                tokenContext = TokenContext.FromFormUrlEncodedContent(requestData!, context.Request.Headers.Authorization, _configuration.Audience!);
                tokenContext.Validate();
            }
            catch (Exception)
            {
                FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: new ArgumentException($"Token request invalid. {tokenContext?.ToLogString() ?? context.ContentString}"), code: HttpStatusCode.BadRequest);
                OnFilterError?.Invoke(this, error);
                return context.SetContextErrorBody(error, _configuration.Debug);
            }

            // Setup new http client for token request
            string tokenEndpoint = "https://login.microsoftonline.com/";
            string tokenPath = $"{_configuration.TenantId}/oauth2/v2.0/token";
            context.UpdateRequestUri(context.Request.Method, tokenEndpoint, tokenPath);

            // Azure AD does not support bare JWKS auth or 384 JWKS auth. We must convert to an associated client secret flow.
            if (tokenContext.GetType() == typeof(BackendServiceTokenContext))
            {
                var castTokenContext = (BackendServiceTokenContext)tokenContext;
                context = await HandleBackendService(context, castTokenContext);
            }
            else
            {
                context.Request.Content = tokenContext.ToFormUrlEncodedContent();
            }

            // TODO - change to "NeedsOrigin" on base token class.
            if (requestData.AllKeys.Contains("code_verifier") && tokenContext.GetType() == typeof(PublicClientTokenContext))
            {
                context.Headers.Add(new HeaderNameValuePair("Origin", "http://localhost", CustomHeaderType.RequestStatic));
            }

            return context;
        }

        private async Task<OperationContext> HandleBackendService(OperationContext context, BackendServiceTokenContext castTokenContext)
        {
            // Check client id to see if it exists. Get JWKS.
            BackendClientConfiguration? clientConfig = null;

            // Fetch the jwks for this client and validate

            try
            {
                clientConfig = await _asymmetricAuthorizationService.AuthenticateBackendAsyncClient(castTokenContext.ClientId, castTokenContext.ClientAssertion);
            }
            catch (HttpRequestException)
            {
                FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: new ConfigurationErrorsException($"JWKS url {clientConfig?.JwksUri?.ToString()} is not responding. Please check the client configuration."));
                OnFilterError?.Invoke(this, error);
                return context.SetContextErrorBody(error, _configuration.Debug);
            }
            catch (Exception ex) when (ex is Microsoft.IdentityModel.Tokens.SecurityTokenValidationException || ex is UnauthorizedAccessException)
            {
                FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: ex, code: HttpStatusCode.Unauthorized);
                OnFilterError?.Invoke(this, error);
                return context.SetContextErrorBody(error, _configuration.Debug);
            }
            catch (Exception ex)
            {
                FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: ex, code: HttpStatusCode.InternalServerError);
                OnFilterError?.Invoke(this, error);
                return context.SetContextErrorBody(error, _configuration.Debug);
            }

            context.Request.Content = castTokenContext.ConvertToClientCredentialsFormUrlEncodedContent(clientConfig.ClientSecret);

            return context;
        }
    }
}
