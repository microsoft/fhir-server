// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.AzureHealth.DataServices.Filters;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SMARTCustomOperations.AzureAuth.Configuration;
using SMARTCustomOperations.AzureAuth.Extensions;
using SMARTCustomOperations.AzureAuth.Models;
using SMARTCustomOperations.AzureAuth.Services;

namespace SMARTCustomOperations.AzureAuth.Filters
{
    public sealed class ContextInfoInputFilter : IInputFilter
    {
        private readonly ILogger _logger;
        private readonly AzureAuthOperationsConfig _configuration;
        private readonly string _id;
        private readonly GraphContextService _graphContextService;

        public ContextInfoInputFilter(ILogger<TokenInputFilter> logger, AzureAuthOperationsConfig configuration, GraphContextService graphContextService)
        {
            _logger = logger;
            _configuration = configuration;
            _id = Guid.NewGuid().ToString();
            _graphContextService = graphContextService;
        }

        public event EventHandler<FilterErrorEventArgs>? OnFilterError;

        public string Name => nameof(ContextInfoInputFilter);

        public StatusType ExecutionStatusType => StatusType.Normal;

        public string Id => _id;

        public async Task<OperationContext> ExecuteAsync(OperationContext context)
        {
            // Only execute for contextInfo request
            if (!context.Request.RequestUri!.LocalPath.Contains("contextInfo", StringComparison.InvariantCultureIgnoreCase))
            {
                return context;
            }

            _logger?.LogInformation("Entered {Name}", Name);

            // Validate token against Microsoft Graph
            string token = context.Request.Headers.Authorization!.Parameter!;
            var userPrincipal = await _graphContextService.ValidateGraphAccessTokenAsync(token);

            if (userPrincipal is null || !userPrincipal.HasClaim(x => x.Type == "oid"))
            {
                FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: new UnauthorizedAccessException("Token validation failed for consent operation"), code: HttpStatusCode.Unauthorized);
                OnFilterError?.Invoke(this, error);
                return context.SetContextErrorBody(error, _configuration.Debug);
            }

            try
            {
                if (context.Request.Method == HttpMethod.Get)
                {
                    var queryParameters = context.Request.RequestUri.ParseQueryString();

                    var clientId = queryParameters["client_id"];
                    var scope = queryParameters["scope"]!.Split(" ");
                    var userId = userPrincipal.FindFirst("oid")!.Value;

                    if (clientId is null || scope is null || userId is null)
                    {
                        FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: new ArgumentException("Get Context Info must contain client_id, scope and token parameters."), code: HttpStatusCode.Unauthorized);
                        OnFilterError?.Invoke(this, error);
                        return context.SetContextErrorBody(error, _configuration.Debug);
                    }

                    var scopes = await _graphContextService.GetAppConsentScopes(clientId!, userId!, scope!.ToList());
                    context.ContentString = JsonConvert.SerializeObject(scopes);
                    return context;
                }
                else
                {
                    var body = JObject.Parse(context.ContentString);
                    var clientId = body.Value<string>("client_id");
                    var userId = body.Value<string>("user_id");
                    var appConsentScopes = body.Value<List<AppConsentScope>>("scopes");

                    if (clientId is null || userId is null || appConsentScopes is null)
                    {
                        FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: new ArgumentException("Post Context Info must contain client_id, user_id and scopes parameters."), code: HttpStatusCode.Unauthorized);
                        OnFilterError?.Invoke(this, error);
                        return context.SetContextErrorBody(error, _configuration.Debug);
                    }

                    await _graphContextService.PersistAppConsentScope(clientId, userId, appConsentScopes);
                    return context;
                }
            }
            catch (Exception ex)
            {
                FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: ex, code: HttpStatusCode.InternalServerError);
                return context.SetContextErrorBody(error, _configuration.Debug);
            }
        }
    }
}
