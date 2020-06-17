// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Microsoft.Health.Fhir.Client
{
    public static class FhirClientAuthExtensions
    {
        /// <summary>
        /// Sets the authenticated token on the FhirClient to the supplied resource via Managed Identity.
        /// </summary>
        /// <param name="fhirClient">The FhirClient to authenticate.</param>
        /// <param name="resource">The resource to obtain a token to.</param>
        /// <returns>A task representing the successful setting of the token.</returns>
        public static async Task AuthenticateWithManagedIdentity(this FhirClient fhirClient, string resource)
        {
            EnsureArg.IsNotNull(fhirClient, nameof(fhirClient));
            EnsureArg.IsNotNullOrWhiteSpace(resource, nameof(resource));

            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            string accessToken = await azureServiceTokenProvider.GetAccessTokenAsync(resource);

            fhirClient.SetBearerToken(accessToken);
        }

        /// <summary>
        /// Sets the authenticated token on the FhirClient via OpenId client credentials.
        /// </summary>
        /// <param name="fhirClient">The FhirClient to authenticate.</param>
        /// <param name="clientId">The clientId of the application.</param>
        /// <param name="clientSecret">The clientSecret of the application.</param>
        /// <param name="resource">The resource to authenticate with.</param>
        /// <param name="scope">The scope to authenticate with.</param>
        /// <returns>A task representing the successful setting of the token.</returns>
        public static async Task AuthenticateOpenIdClientCredentials(
            this FhirClient fhirClient,
            string clientId,
            string clientSecret,
            string resource,
            string scope)
        {
            EnsureArg.IsNotNull(fhirClient, nameof(fhirClient));
            EnsureArg.IsNotNullOrWhiteSpace(clientId, nameof(clientId));
            EnsureArg.IsNotNullOrWhiteSpace(clientSecret, nameof(clientSecret));
            EnsureArg.IsNotNullOrWhiteSpace(resource, nameof(resource));
            EnsureArg.IsNotNullOrWhiteSpace(scope, nameof(scope));

            var formData = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(OpenIdConnectParameterNames.ClientId, clientId),
                new KeyValuePair<string, string>(OpenIdConnectParameterNames.ClientSecret, clientSecret),
                new KeyValuePair<string, string>(OpenIdConnectParameterNames.GrantType, OpenIdConnectGrantTypes.ClientCredentials),
                new KeyValuePair<string, string>(OpenIdConnectParameterNames.Scope, scope),
                new KeyValuePair<string, string>(OpenIdConnectParameterNames.Resource, resource),
            };

            await ObtainTokenAndSetOnClient(fhirClient, formData);
        }

        /// <summary>
        /// Sets the authenticated token on the FhirClient via OpenId user password.
        /// </summary>
        /// <param name="fhirClient">The FhirClient to authenticate.</param>
        /// <param name="clientId">The clientId of the application.</param>
        /// <param name="clientSecret">The clientSecret of the application.</param>
        /// <param name="resource">The resource to authenticate with.</param>
        /// <param name="scope">The scope to authenticate with.</param>
        /// <param name="username">The username to authenticate.</param>
        /// <param name="password">The password to authenticate.</param>
        /// <returns>A task representing the successful setting of the token.</returns>
        public static async Task AuthenticateOpenIdUserPassword(
            this FhirClient fhirClient,
            string clientId,
            string clientSecret,
            string resource,
            string scope,
            string username,
            string password)
        {
            var formData = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(OpenIdConnectParameterNames.ClientId, clientId),
                new KeyValuePair<string, string>(OpenIdConnectParameterNames.ClientSecret, clientSecret),
                new KeyValuePair<string, string>(OpenIdConnectParameterNames.GrantType, OpenIdConnectGrantTypes.Password),
                new KeyValuePair<string, string>(OpenIdConnectParameterNames.Scope, scope),
                new KeyValuePair<string, string>(OpenIdConnectParameterNames.Resource, resource),
                new KeyValuePair<string, string>(OpenIdConnectParameterNames.Username, username),
                new KeyValuePair<string, string>(OpenIdConnectParameterNames.Password, password),
            };

            await ObtainTokenAndSetOnClient(fhirClient, formData);
        }

        private static async Task ObtainTokenAndSetOnClient(FhirClient fhirClient, List<KeyValuePair<string, string>> formData)
        {
            using var formContent = new FormUrlEncodedContent(formData);
            using HttpResponseMessage tokenResponse = await fhirClient.HttpClient.PostAsync(fhirClient.TokenUri, formContent);

            var openIdConnectMessage = new OpenIdConnectMessage(await tokenResponse.Content.ReadAsStringAsync());
            fhirClient.SetBearerToken(openIdConnectMessage.AccessToken);
        }
    }
}
