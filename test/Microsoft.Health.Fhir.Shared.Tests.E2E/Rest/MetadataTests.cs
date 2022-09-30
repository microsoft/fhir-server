// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class MetadataTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public MetadataTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenInvalidFormatParameter_WhenGettingMetadata_TheServerShouldReturnNotAcceptable()
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(async () => await _client.ReadAsync<CapabilityStatement>("metadata?_format=blah"));
            Assert.Equal(HttpStatusCode.NotAcceptable, ex.StatusCode);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("0")]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenInvalidPrettyParameter_WhenGettingMetadata_TheServerShouldReturnBadRequest(string value)
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(async () => await _client.ReadAsync<CapabilityStatement>($"metadata?_pretty={value}"));
            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("")]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenInvalidSummaryParameter_WhenGettingMetadata_TheServerShouldReturnBadRequest(string value)
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(async () => await _client.ReadAsync<CapabilityStatement>($"metadata?_summary={value}"));
            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Theory]
        [InlineData(" ")]
        [InlineData("")]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenInvalidElementsParameter_WhenGettingMetadata_TheServerShouldReturnBadRequest(string value)
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(async () => await _client.ReadAsync<CapabilityStatement>($"metadata?_elements={value}"));
            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenBothDataStores_WhenGettingMetadata_TheServerShouldReturnPatientEverythingSupported()
        {
            FhirResponse<CapabilityStatement> capabilityStatement = await _client.ReadAsync<CapabilityStatement>("metadata");

            object operationDefinition = ModelInfoProvider.Version == FhirSpecification.Stu3
                ? capabilityStatement.Resource.ToTypedElement().Scalar($"CapabilityStatement.rest.operation.where(name = '{OperationsConstants.PatientEverything}').definition.reference")
                : capabilityStatement.Resource.ToTypedElement().Scalar($"CapabilityStatement.rest.operation.where(name = '{OperationsConstants.PatientEverything}').definition");

            Assert.Equal(OperationsConstants.PatientEverythingUri, operationDefinition.ToString());
        }
    }
}
