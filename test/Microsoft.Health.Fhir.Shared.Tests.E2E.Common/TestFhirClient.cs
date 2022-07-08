// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Health.Client.Authentication;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using FhirClient = Microsoft.Health.Fhir.Client.FhirClient;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public class TestFhirClient : FhirClient
    {
        private readonly TestFhirServer _testFhirServer;
        private readonly TestApplication _clientApplication;
        private readonly TestUser _user;

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

        public TestFhirClient Clone(AuthenticationHttpMessageHandler authenticationHandler = null)
        {
            return _testFhirServer.GetTestFhirClient(Format, _clientApplication, _user, reusable: false, authenticationHandler);
        }
    }
}
