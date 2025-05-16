// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.ConditionalOperations)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class ConditionalCreateTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
    {
        private readonly TestFhirClient _client;

        public ConditionalCreateTests(HttpIntegrationTestFixture<Startup> fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenCreatingConditionallyWithNoIdAndNoExisting_TheServerShouldReturnTheResourceSuccessfully()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = null;

            using FhirResponse<Observation> updateResponse = await _client.CreateAsync(
                observation,
                $"identifier={Guid.NewGuid().ToString()}");

            Assert.Equal(HttpStatusCode.Created, updateResponse.StatusCode);

            Observation updatedResource = updateResponse.Resource;

            Assert.NotNull(updatedResource);
            Assert.NotNull(updatedResource.Id);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResourceWithNoId_WhenCreatingConditionallyWithOneMatch_TheServerShouldReturnOK()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            var identifier = Guid.NewGuid().ToString();

            observation.Identifier.Add(new Identifier("http://e2etests", identifier));
            using FhirResponse<Observation> response = await _client.CreateAsync(observation);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();

            using FhirResponse<Observation> updateResponse = await _client.CreateAsync(
                observation2,
                $"identifier={identifier}");

            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            Assert.NotNull(updateResponse.Resource);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResourceAndProvenanceHeader_WhenCreatingConditionallyWithNoIdAndNoExisting_TheServerShouldRespondSuccessfully()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = null;

            using FhirResponse<Observation> updateResponse = await _client.CreateAsync(
                observation,
                $"identifier={Guid.NewGuid().ToString()}",
                Samples.GetProvenanceHeader());

            Assert.Equal(HttpStatusCode.Created, updateResponse.StatusCode);

            Observation updatedResource = updateResponse.Resource;

            Assert.NotNull(updatedResource);
            Assert.NotNull(updatedResource.Id);

            using var provenanceResponse = await _client.SearchAsync(ResourceType.Provenance, $"target={observation.Id}");
            Assert.Equal(HttpStatusCode.OK, provenanceResponse.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResourceAndMalformedProvenanceHeader_WhenPostingToHttp_TheServerShouldRespondSuccessfully()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = null;
            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>(), $"identifier={Guid.NewGuid().ToString()}", "Jibberish"));
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenCreatingConditionallyWithMultipleMatches_TheServerShouldFail()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            var identifier = Guid.NewGuid().ToString();

            observation.Identifier.Add(new Identifier("http://e2etests", identifier));

            using FhirResponse<Observation> response = await _client.CreateAsync(observation);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using FhirResponse<Observation> response2 = await _client.CreateAsync(observation);
            Assert.Equal(HttpStatusCode.Created, response2.StatusCode);

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation2.Id = Guid.NewGuid().ToString();

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.CreateAsync(
                observation2,
                $"identifier={identifier}"));

            Assert.Equal(HttpStatusCode.PreconditionFailed, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenCreatingConditionallyWithEmptyIfNoneHeader_TheServerShouldFail()
        {
            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.CreateAsync(
                Samples.GetDefaultObservation().ToPoco<Observation>(),
                "&"));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
            Assert.Single(exception.OperationOutcome.Issue);
            Assert.Equal(exception.Response.Resource.Issue[0].Diagnostics, string.Format(Core.Resources.ConditionalOperationNotSelectiveEnough, "Observation"));
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData(null, true)]
        [InlineData("return=minimal", true)]
        [InlineData("return=representation", true)]
        [InlineData("return=OperationOutcome", true)]
        [InlineData("return=", false)]
        public async Task GivenAMatchedResource_WhenCreatingConditionallyWithPreferHeader_TheResponseShouldHaveCorrectContentAndHeaders(
            string returnPreference,
            bool valid)
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            var identifier = Guid.NewGuid().ToString();

            observation.Identifier.Add(new Identifier("http://e2etests", identifier));
            using FhirResponse<Observation> response = await _client.CreateAsync(observation);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();

            Dictionary<string, string> additionalHeaders = null;
            if (!string.IsNullOrWhiteSpace(returnPreference))
            {
                additionalHeaders = new Dictionary<string, string>
                {
                    { KnownHeaders.Prefer, returnPreference },
                };
            }

            try
            {
                using FhirResponse<Resource> updateResponse = await _client.CreateAsync<Resource>(
                    resource: observation2,
                    conditionalCreateCriteria: $"identifier={identifier}",
                    additionalHeaders: additionalHeaders);

                Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
                Assert.Equal(response.Response.Headers.ETag?.ToString(), updateResponse.Response.Headers.ETag?.ToString());
                Assert.Equal(response.Response.Content.Headers.LastModified, updateResponse.Response.Content.Headers.LastModified);
                Assert.True(!string.IsNullOrWhiteSpace(updateResponse.Response.Headers.Location?.AbsolutePath));

                if (string.IsNullOrWhiteSpace(returnPreference) || returnPreference.Equals("return=representation", StringComparison.Ordinal))
                {
                    Assert.NotNull(updateResponse.Resource);
                    Assert.Equal(response.Resource.TypeName, updateResponse.Resource.TypeName);
                    Assert.Equal(response.Resource.Id, updateResponse.Resource.Id);
                }
                else if (returnPreference.Equals("return=minimal", StringComparison.Ordinal))
                {
                    Assert.Null(updateResponse.Resource);
                }
                else if (returnPreference.Equals("return=OperationOutcome", StringComparison.Ordinal))
                {
                    Assert.NotNull(updateResponse.Resource);
                    Assert.Equal(KnownResourceTypes.OperationOutcome, updateResponse.Resource.TypeName);
                    Assert.Contains(
                        ((OperationOutcome)updateResponse.Resource).Issue,
                        x => x.Severity == OperationOutcome.IssueSeverity.Information && !string.IsNullOrEmpty(x.Details?.Text));
                }

                Assert.True(valid);
            }
            catch (FhirClientException ex)
            {
                Assert.False(valid);
                Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
            }
        }
    }
}
