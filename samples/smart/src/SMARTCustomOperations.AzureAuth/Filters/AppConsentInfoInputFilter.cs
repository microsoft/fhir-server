// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Security.Claims;
using Microsoft.AzureHealth.DataServices.Filters;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using SMARTCustomOperations.AzureAuth.Configuration;
using SMARTCustomOperations.AzureAuth.Extensions;
using SMARTCustomOperations.AzureAuth.Models;
using SMARTCustomOperations.AzureAuth.Services;

namespace SMARTCustomOperations.AzureAuth.Filters
{
    public sealed class AppConsentInfoInputFilter : IInputFilter
    {
        private readonly ILogger _logger;
        private readonly AzureAuthOperationsConfig _configuration;
        private readonly string _id;
        private readonly GraphConsentService _graphContextService;

        public AppConsentInfoInputFilter(ILogger<TokenInputFilter> logger, AzureAuthOperationsConfig configuration, GraphConsentService graphContextService)
        {
            _logger = logger;
            _configuration = configuration;
            _id = Guid.NewGuid().ToString();
            _graphContextService = graphContextService;
        }

        public event EventHandler<FilterErrorEventArgs>? OnFilterError;

        public string Name => nameof(AppConsentInfoInputFilter);

        public StatusType ExecutionStatusType => StatusType.Normal;

        public string Id => _id;

        public async Task<OperationContext> ExecuteAsync(OperationContext context)
        {
            // Only execute for contextInfo request
            if (!context.Request.RequestUri!.LocalPath.Contains("appConsentInfo", StringComparison.InvariantCultureIgnoreCase))
            {
                return context;
            }

            _logger?.LogInformation("Entered {Name}", Name);

            // Validate token against Microsoft Graph
            ClaimsPrincipal userPrincipal;
            try
            {
                string token = context.Request.Headers.Authorization!.Parameter!;
                userPrincipal = await _graphContextService.ValidateGraphAccessTokenAsync(token);
            }
            catch (Exception ex) when (ex is Microsoft.IdentityModel.Tokens.SecurityTokenValidationException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning("User attempted to access app consent info without a valid token. {Exception}", ex);
                FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: ex, code: HttpStatusCode.Unauthorized);
                OnFilterError?.Invoke(this, error);
                return context.SetContextErrorBody(error, _configuration.Debug);
            }
            catch (Exception ex)
            {
                _logger.LogCritical("Unknown error while validating user token. {Exception}", ex);
                FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: ex, code: HttpStatusCode.InternalServerError);
                OnFilterError?.Invoke(this, error);
                return context.SetContextErrorBody(error, _configuration.Debug);
            }

            if (userPrincipal is null || !userPrincipal.HasClaim(x => x.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier"))
            {
                _logger.LogError("User does not have the oid claimin AppConsentInfoInputFilter. {User}", userPrincipal);
                FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: new UnauthorizedAccessException("Token validation failed for get context info operation"), code: HttpStatusCode.Unauthorized);
                OnFilterError?.Invoke(this, error);
                return context.SetContextErrorBody(error, _configuration.Debug);
            }

            if (context.Request.Method == HttpMethod.Get)
            {
                AuthorizeContext uriContext = new(context.Request.RequestUri.ParseQueryString());
                var userId = userPrincipal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")!.Value;

                if (uriContext.ClientId is null || uriContext.Scope is null || userId is null)
                {
                    FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: new ArgumentException("Get Context Info must contain client_id, scope and token parameters."), code: HttpStatusCode.BadRequest);
                    OnFilterError?.Invoke(this, error);
                    return context.SetContextErrorBody(error, _configuration.Debug);
                }

                var scopes = uriContext.Scope.ParseScope(string.Empty).Split(" ");

                try
                {
                    var info = await _graphContextService.GetAppConsentScopes(uriContext.ClientId, userId!, scopes);
                    context.ContentString = JsonConvert.SerializeObject(info, new JsonSerializerSettings() { ContractResolver = new CamelCasePropertyNamesContractResolver() });
                    context.StatusCode = HttpStatusCode.OK;
                    return context;
                }
                catch (Microsoft.Graph.ServiceException ex)
                {
                    _logger.LogError(ex, "Fatal error calling Microsoft Graph to get Consent Inforation. {Uri} {CliendId} {UserId} {Scopes}", context.Request.RequestUri, uriContext.ClientId, userId!, scopes);
                    FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: ex, code: HttpStatusCode.InternalServerError, responseBody: context.ContentString);
                    context.StatusCode = HttpStatusCode.InternalServerError;
                    return context.SetContextErrorBody(error, _configuration.Debug);
                }
            }
            else
            {
                var body = JObject.Parse(context.ContentString);
                var appConsentInfo = body.ToObject<AppConsentInfo>();
                var userId = userPrincipal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")!.Value;

                if (appConsentInfo?.ApplicationId is null || userId is null || appConsentInfo?.Scopes is null)
                {
                    FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: new ArgumentException("Post Context Info must contain application id and scope parameters. Token must have valid user information."), code: HttpStatusCode.BadRequest);
                    OnFilterError?.Invoke(this, error);
                    return context.SetContextErrorBody(error, _configuration.Debug);
                }

                // TODO - catch unauthorized exception and return 500
                // Graph will reprompt for scopes if needed. We only care about removal.
                try
                {
                    await _graphContextService.PersistAppConsentScopeIfRemoval(appConsentInfo, userId);
                    context.StatusCode = HttpStatusCode.NoContent;
                    return context;
                }
                catch (Microsoft.Graph.ServiceException ex)
                {
                    _logger.LogError(ex, "Fatal error calling Microsoft Graph to update Consent Inforation. {Uri} {AppInformation} {UserId}", context.Request.RequestUri, appConsentInfo, userId);
                    FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: ex, code: HttpStatusCode.InternalServerError, responseBody: context.ContentString);
                    context.StatusCode = HttpStatusCode.InternalServerError;
                    return context.SetContextErrorBody(error, _configuration.Debug);
                }
            }
        }
    }
}
