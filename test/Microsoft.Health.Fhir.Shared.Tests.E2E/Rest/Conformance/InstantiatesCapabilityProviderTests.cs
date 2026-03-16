// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Polly;
using Polly.Retry;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class InstantiatesCapabilityProviderTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private const string USCore6CapabilityStatementUrl = "http://hl7.org/fhir/us/core/CapabilityStatement/us-core-server";
        private const string USCore6PatientProfileFileName = "StructureDefinition-us-core-patient-v6";

        private readonly HttpIntegrationTestFixture _fixture;
        private readonly AsyncRetryPolicy _retryPolicy;

        public InstantiatesCapabilityProviderTests(HttpIntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 10,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(3));
        }

        private TestFhirClient Client => _fixture.TestFhirClient;

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenMetadataRequest_WhenUSCore6ProfileIsUploaded_TheInstantiatesFieldShouldBePopulated(
            bool uploadUSCore6Profile)
        {
            var resource = default(StructureDefinition);
            try
            {
                var exists = await USCore6ProfileExists();
                Skip.If(
                    !uploadUSCore6Profile && exists,
                    "USCore 6 profile already uploaded on the server by other tests. Skipping test.");

                if (uploadUSCore6Profile && !exists)
                {
                    resource = await UploadUSCore6PatientProfileAsync();
                }

                await _retryPolicy.ExecuteAsync(
                    async () =>
                    {
                        var response = await Client.ReadAsync<CapabilityStatement>("metadata");
                        Assert.NotNull(response?.Resource);
                        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                        var instantiates = response.Resource.Instantiates?.ToList() ?? new List<string>();
                        if (uploadUSCore6Profile)
                        {
                            Assert.Contains(
                                instantiates,
                                x =>
                                {
                                    return string.Equals(x, USCore6CapabilityStatementUrl, StringComparison.OrdinalIgnoreCase);
                                });
                        }
                        else
                        {
                            Assert.Empty(instantiates);
                        }
                    });
            }
            finally
            {
                if (resource != null)
                {
                    await Client.HardDeleteAsync(resource);
                }
            }
        }

        private async Task<StructureDefinition> UploadUSCore6PatientProfileAsync()
        {
            var patientProfile = Samples.GetJsonSample<StructureDefinition>(USCore6PatientProfileFileName);
            var response = await Client.CreateAsync(patientProfile);
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            await _retryPolicy.ExecuteAsync(
                async () =>
                {
                    var exists = await USCore6ProfileExists();
                    Assert.True(exists, "US Core 6 profile was not found after upload.");
                });
            return response.Resource;
        }

        private async Task<bool> USCore6ProfileExists()
        {
            var url = $"{KnownResourceTypes.StructureDefinition}?url:below=http://hl7.org/fhir/us/core/StructureDefinition/&version=6.0.0&_summary=count";
            var result = await Client.SearchAsync(url);
            return result.Resource?.Total > 0;
        }
    }
}
