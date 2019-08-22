// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// A test fixture that is intended to provide a <see cref="FhirClient"/> to end-to-end FHIR test classes.
    /// </summary>
    /// <typeparam name="TStartup">The type to use as the ASP.NET startup type when hosting the fhir server in-process</typeparam>
    public class HttpIntegrationTestFixture<TStartup>
    {
        public HttpIntegrationTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
        {
            EnsureArg.IsNotNull(testFhirServerFactory, nameof(testFhirServerFactory));

            TestFhirServer = testFhirServerFactory.GetTestFhirServer(dataStore, typeof(TStartup));

            ResourceFormat resourceFormat;
            switch (format)
            {
                case Format.Json:
                    resourceFormat = ResourceFormat.Json;
                    break;
                case Format.Xml:
                    resourceFormat = ResourceFormat.Xml;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, null);
            }

            FhirClient = TestFhirServer.GetFhirClient(resourceFormat);

            IsUsingInProcTestServer = TestFhirServer is InProcTestFhirServer;
        }

        public bool IsUsingInProcTestServer { get; }

        public HttpClient HttpClient => FhirClient.HttpClient;

        public FhirClient FhirClient { get; }

        protected TestFhirServer TestFhirServer { get; }

        public string GenerateFullUrl(string relativeUrl)
        {
            return $"{TestFhirServer.BaseAddress}{relativeUrl}";
        }
    }
}
