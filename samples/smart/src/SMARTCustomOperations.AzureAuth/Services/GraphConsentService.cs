// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SMARTCustomOperations.AzureAuth.Configuration;
using SMARTCustomOperations.AzureAuth.Models;

#pragma warning disable CA1002 // Do not expose generic lists

namespace SMARTCustomOperations.AzureAuth.Services
{
    public class GraphConsentService
    {
        private readonly ILogger<GraphConsentService> _logger;
        private readonly GraphServiceClient _graphServiceClient;
        private readonly bool _debug;
        private readonly string _contextAppClientId;
        private readonly string _tenantId;
        private readonly Dictionary<string, ServicePrincipal> _resourceServicePrincipals = new();

        public GraphConsentService(AzureAuthOperationsConfig configuration, ILogger<GraphConsentService> logger)
        {
            _graphServiceClient = new GraphServiceClient(new DefaultAzureCredential());
            _logger = logger;
            _debug = configuration.Debug;
            _contextAppClientId = configuration.ContextAppClientId!;
            _tenantId = configuration.TenantId!;
        }

        // https://github.com/Azure-Samples/ms-identity-dotnet-webapi-azurefunctions/blob/master/Function/BootLoader.cs
        public async Task<ClaimsPrincipal> ValidateGraphAccessTokenAsync(string accessToken)
        {
            var authority = $"https://login.microsoftonline.com/{_tenantId}/v2.0";
            var validIssuers = new List<string>()
            {
                $"https://login.microsoftonline.com/{_tenantId}/",
                $"https://login.microsoftonline.com/{_tenantId}/v2.0",
                $"https://login.windows.net/{_tenantId}/",
                $"https://login.microsoft.com/{_tenantId}/",
                $"https://sts.windows.net/{_tenantId}/",
            };

            // Debugging purposes only, set this to false for production
            if (_debug)
            {
                Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
            }

            ConfigurationManager<OpenIdConnectConfiguration> configManager =
                new ConfigurationManager<OpenIdConnectConfiguration>(
                    $"{authority}/.well-known/openid-configuration",
                    new OpenIdConnectConfigurationRetriever());

            OpenIdConnectConfiguration config = await configManager.GetConfigurationAsync();

            ISecurityTokenValidator tokenValidator = new JwtSecurityTokenHandler();

            // Initialize the token validation parameters
            TokenValidationParameters validationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidateIssuer = true,
                ValidateIssuerSigningKey = true,

                // App Id URI and AppId of this service application are both valid audiences.
                ValidAudiences = new[] { _contextAppClientId, $"api://{_contextAppClientId}" },

                // Support Azure AD V1 and V2 endpoints.
                ValidIssuers = validIssuers,
                IssuerSigningKeys = config.SigningKeys,
            };

            SecurityToken securityToken;
            var claimsPrincipal = tokenValidator.ValidateToken(accessToken, validationParameters, out securityToken);
            return claimsPrincipal;
        }

        public async Task<AppConsentInfo> GetAppConsentScopes(string requestingAppClientId, string userId, List<string> requestedScopes)
        {
            // Get needed objects from graph
            var requestingClientApp = await GetRequestingApplication(requestingAppClientId);
            var resourceServicePrincipals = await GetResourceServicePrincipals(requestingClientApp);
            var requestingClientAppScopeIds = GetAppScopeIds(requestingClientApp);
            var requestingServicePrincipal = await GetRequestingServicePrincipal(requestingAppClientId);
            var permissions = await GetUserAppOAuth2PermissinGrants(requestingServicePrincipal.Id, userId);

            AppConsentInfo info = new();
            info.ApplicationName = requestingClientApp.DisplayName;
            info.ApplicationDescription = requestingClientApp.Description;
            info.ApplicationUrl = requestingClientApp.Info.MarketingUrl;

            foreach (string scope in requestedScopes)
            {
                var matchingResourcePrincipal = resourceServicePrincipals.Values.Where(x => x.Oauth2PermissionScopes.Any(y => requestingClientAppScopeIds.Contains((Guid)y.Id!) && y.Value == scope)).FirstOrDefault();
                var scopeInfo = matchingResourcePrincipal?.Oauth2PermissionScopes.Where(x => x.Value == scope).FirstOrDefault();
                var scopeConsentRecord = permissions.SingleOrDefault(x => x.ResourceId == matchingResourcePrincipal?.Id && x.Scope == scope);

                if (matchingResourcePrincipal is not null && scopeInfo?.Id is not null)
                {
                    info.Scopes.Add(new AppConsentScope()
                    {
                        Name = scopeInfo.Value,
                        Id = scopeInfo.Id.ToString(),
                        ResourceId = matchingResourcePrincipal?.Id,
                        Consented = scopeConsentRecord is not null,
                        ConsentId = scopeConsentRecord?.Id,
                        UserDescription = scopeInfo.UserConsentDescription,
                    });
                }
            }

            return info;
        }

        public async Task PersistAppConsentScope(string requestingAppClientId, string userId, List<AppConsentScope> scopeList)
        {
            foreach (var resourceId in scopeList.Where(x => x is not null).Select(x => x.ResourceId).Distinct())
            {
                var resourceScopes = scopeList.Where(x => x.ResourceId == resourceId).ToList();
                if (resourceScopes.Any(x => x.ConsentId is not null))
                {
                    await UpdateUserAppOAuth2PermissionGrant(resourceScopes.First(x => x.ConsentId is not null).ConsentId!, string.Join(" ", resourceScopes));
                }
                else if (resourceScopes.Any())
                {
                    var requestingServicePrincipal = await GetRequestingServicePrincipal(requestingAppClientId);
                    await CreateUserAppOAuth2PermissinGrant(requestingServicePrincipal.Id, userId, resourceId!, string.Join(" ", resourceScopes));
                }
            }
        }

        private async Task<Application> GetRequestingApplication(string applicationId)
        {
            var requestingApp = await _graphServiceClient.Applications.Request().Filter($"appId eq '{applicationId}'").GetAsync();

            if (requestingApp.Count != 1)
            {
                throw new ArgumentException($"Could not find single application for app id {applicationId}");
            }

            return requestingApp.First();
        }

        private async Task<ServicePrincipal> GetRequestingServicePrincipal(string applicationId)
        {
            var requestingServicePrincipal = await _graphServiceClient.ServicePrincipals.Request().Filter($"appId eq '{applicationId}'").GetAsync();

            if (requestingServicePrincipal.Count != 1)
            {
                throw new ArgumentException($"Could not find single Service Principal for app id {applicationId}");
            }

            return requestingServicePrincipal.First();
        }

        private static List<Guid> GetAppScopeIds(Application clientApp)
        {
            List<Guid> appScopeIds = new();

            foreach (var resource in clientApp.RequiredResourceAccess)
            {
                foreach (var scope in resource.ResourceAccess)
                {
                    if (scope?.Type == "Scope" && scope?.Id is not null)
                    {
                        appScopeIds.Add((Guid)scope.Id);
                    }
                }
            }

            return appScopeIds;
        }

        private async Task<Dictionary<string, ServicePrincipal>> GetResourceServicePrincipals(Application clientApp)
        {
            foreach (var resourceAppId in clientApp.RequiredResourceAccess.Select(x => x.ResourceAppId).Distinct())
            {
                if (_resourceServicePrincipals.ContainsKey(resourceAppId))
                {
                    continue;
                }

                var servicePrincipal = await _graphServiceClient.ServicePrincipals.Request().Filter($"appId eq '{resourceAppId}'").GetAsync();

                if (servicePrincipal.Count != 1)
                {
                    throw new ArgumentException($"Could not find single service principal for resource app id {resourceAppId}");
                }

                _resourceServicePrincipals.Add(resourceAppId, servicePrincipal.First());
            }

            return _resourceServicePrincipals;
        }

        private async Task<List<OAuth2PermissionGrant>> GetUserAppOAuth2PermissinGrants(string requestingAppClientId, string userId)
        {
            var permissionPage = await _graphServiceClient.Oauth2PermissionGrants.Request().Filter($"clientId eq '{requestingAppClientId}' and consentType eq 'Principal' and principalId eq '{userId}'").GetAsync();
            return permissionPage.ToList();
        }

        private async Task<OAuth2PermissionGrant> CreateUserAppOAuth2PermissinGrant(string servicePrincipalId, string userId, string resourceId, string scope)
        {
            var permission = new OAuth2PermissionGrant
            {
                ClientId = servicePrincipalId,
                ConsentType = "Principal",
                PrincipalId = userId,
                ResourceId = resourceId,
                Scope = scope,
            };

            return await _graphServiceClient.Oauth2PermissionGrants.Request().AddAsync(permission);
        }

        private async Task UpdateUserAppOAuth2PermissionGrant(string grantId, string scope)
        {
            var permission = new OAuth2PermissionGrant
            {
                Scope = scope,
            };

            await _graphServiceClient.Oauth2PermissionGrants[grantId].Request().UpdateAsync(permission);
        }
    }
}
