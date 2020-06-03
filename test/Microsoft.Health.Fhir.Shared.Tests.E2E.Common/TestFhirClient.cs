// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using FhirClient = Microsoft.Health.Fhir.Client.FhirClient;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public class TestFhirClient : FhirClient
    {
        private readonly TestFhirServer _testFhirServer;
        private readonly TestApplication _clientApplication;
        private readonly TestUser _user;
        private readonly Dictionary<string, string> _bearerTokens = new Dictionary<string, string>();

        public TestFhirClient(
            HttpClient httpClient,
            TestFhirServer testFhirServer,
            ResourceFormat format,
            TestApplication clientApplication,
            TestUser user)
        : base(httpClient, format)
        {
            _testFhirServer = testFhirServer;
            _clientApplication = clientApplication;
            _user = user;

            ConfigureSecurityOptions();
            SetupAuthenticationAsync(clientApplication, user).GetAwaiter().GetResult();
        }

        public TestFhirClient CreateClientForUser(TestUser user, TestApplication clientApplication)
        {
            EnsureArg.IsNotNull(user, nameof(user));
            EnsureArg.IsNotNull(clientApplication, nameof(clientApplication));
            return _testFhirServer.GetTestFhirClient(Format, clientApplication, user);
        }

        public TestFhirClient CreateClientForClientApplication(TestApplication clientApplication)
        {
            EnsureArg.IsNotNull(clientApplication, nameof(clientApplication));
            return _testFhirServer.GetTestFhirClient(Format, clientApplication, null);
        }

        public TestFhirClient Clone()
        {
            return _testFhirServer.GetTestFhirClient(Format, _clientApplication, _user, reusable: false);
        }

        private async Task SetupAuthenticationAsync(TestApplication clientApplication, TestUser user = null)
        {
            if (SecurityEnabled == true)
            {
                var tokenKey = $"{clientApplication.ClientId}:{(user == null ? string.Empty : user.UserId)}";

                if (!_bearerTokens.ContainsKey(tokenKey))
                {
                    await Authenticate(clientApplication, user);
                    _bearerTokens[tokenKey] = HttpClient.DefaultRequestHeaders?.Authorization?.Parameter;
                }
                else
                {
                    SetBearerToken(_bearerTokens[tokenKey]);
                }
            }
        }

        private async Task Authenticate(TestApplication clientApplication, TestUser user)
        {
            if (clientApplication.Equals(TestApplications.InvalidClient))
            {
                return;
            }

            if (user == null)
            {
                string scope = clientApplication.Equals(TestApplications.WrongAudienceClient) ? clientApplication.ClientId : AuthenticationSettings.Scope;
                string resource = clientApplication.Equals(TestApplications.WrongAudienceClient) ? clientApplication.ClientId : AuthenticationSettings.Resource;

                await this.AuthenticateOpenIdClientCredentials(
                    clientApplication.ClientId,
                    clientApplication.ClientSecret,
                    resource,
                    scope);
            }
            else
            {
                await this.AuthenticateOpenIdUserPassword(
                    clientApplication.ClientId,
                    clientApplication.ClientSecret,
                    AuthenticationSettings.Resource,
                    AuthenticationSettings.Scope,
                    user.UserId,
                    user.Password);
            }
        }
    }
}
