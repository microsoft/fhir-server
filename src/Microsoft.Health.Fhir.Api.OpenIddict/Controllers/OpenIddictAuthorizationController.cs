// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Microsoft.Health.Fhir.Api.OpenIddict.Controllers
{
    public class OpenIddictAuthorizationController : Controller
    {
        private readonly AuthorizationConfiguration _authorizationConfiguration;
        private readonly IOpenIddictApplicationManager _applicationManager;
        private readonly IOpenIddictScopeManager _scopeManager;

        public OpenIddictAuthorizationController(
            AuthorizationConfiguration authorizationConfiguration,
            IOpenIddictApplicationManager applicationManager,
            IOpenIddictScopeManager scopeManager)
        {
            EnsureArg.IsNotNull(authorizationConfiguration, nameof(authorizationConfiguration));
            EnsureArg.IsNotNull(applicationManager, nameof(applicationManager));
            EnsureArg.IsNotNull(scopeManager, nameof(scopeManager));

            _authorizationConfiguration = authorizationConfiguration;
            _applicationManager = applicationManager;
            _scopeManager = scopeManager;
        }

        [HttpPost]
        [Route("/connect/token")]
        [AllowAnonymous]
        public async Task<IActionResult> Token()
        {
            var feature = HttpContext.Features.Get<OpenIddictServerAspNetCoreFeature>();
            var transaction = feature?.Transaction;
            var request = transaction?.Request;
            if (request == null)
            {
                throw new RequestNotValidException("Invalid request: null");
            }

            if (!request.IsClientCredentialsGrantType())
            {
                throw new RequestNotValidException($"Invalid request grant type: {request.GrantType}. It must be '{OpenIddictConstants.GrantTypes.ClientCredentials}'.");
            }

            var application = await _applicationManager.FindByClientIdAsync(request.ClientId);
            if (application == null)
            {
                throw new RequestNotValidException($"Unknown client application: {request.ClientId}.");
            }

            // Create the claims-based identity that will be used by OpenIddict to generate tokens.
            var identity = new ClaimsIdentity(
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role);

            // Add the claims that will be persisted in the tokens (use the client_id as the subject identifier).
            identity.SetClaim(Claims.Subject, await _applicationManager.GetClientIdAsync(application));
            identity.SetClaim(Claims.Name, await _applicationManager.GetDisplayNameAsync(application));
            identity.SetClaim("fhirUser", CreateFhirUserClaim(request.ClientId, HttpContext.Request.Host.ToString()));

            var permissions = await _applicationManager.GetPermissionsAsync(application);
            var roles = permissions.Where(x => x.StartsWith($"{_authorizationConfiguration.RolesClaim}:", StringComparison.Ordinal));
            foreach (var role in roles)
            {
                var r = role?.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (r?.Length == 2)
                {
                    identity.SetClaim(_authorizationConfiguration.RolesClaim, r[^1]);
                }
            }

            // Set the list of scopes granted to the client application in access_token.
            identity.SetScopes(request.GetScopes());
            var resources = await ToListAsync(_scopeManager.ListResourcesAsync(identity.GetScopes()));
            resources.Add("fhir-api");
            identity.SetResources(resources);

            // Add a custom claim for the raw scope with dynamic query parameters.
            if (transaction.Properties.TryGetValue("raw_scope", out var rawScopeObj) && rawScopeObj is string rawScope)
            {
                identity.SetClaim("raw_scope", rawScope);
            }

            identity.SetDestinations(GetDestinations);

            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        private static IEnumerable<string> GetDestinations(System.Security.Claims.Claim claim)
        {
            // Note: by default, claims are NOT automatically included in the access and identity tokens.
            // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
            // whether they should be included in access tokens, in identity tokens or in both.

            return claim.Type switch
            {
                Claims.Name or Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],

                _ => [Destinations.AccessToken],
            };
        }

        private static Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return ExecuteAsync();

            async Task<List<T>> ExecuteAsync()
            {
                var list = new List<T>();

                await foreach (var element in source)
                {
                    list.Add(element);
                }

                return list;
            }
        }

        private static string CreateFhirUserClaim(string userId, string host)
        {
            string userType = null;

            if (userId.Contains("patient", StringComparison.OrdinalIgnoreCase))
            {
                userType = "Patient";
            }
            else if (userId.Contains("practitioner", StringComparison.OrdinalIgnoreCase))
            {
                userType = "Practitioner";
            }
            else if (userId.Contains("system", StringComparison.OrdinalIgnoreCase))
            {
                userType = "System";
            }
            else if (userId.Contains("smartUserClient", StringComparison.OrdinalIgnoreCase))
            {
                userType = "Patient";
            }

            return $"https://{host}/{userType}/" + userId;
        }
    }
}
