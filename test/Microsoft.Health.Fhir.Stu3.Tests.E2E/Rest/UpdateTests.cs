// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public partial class UpdateTests : IClassFixture<HttpIntegrationTestFixture>
    {
        [Theory]
        [InlineData("invalidVersion")]
        [InlineData("-1")]
        [InlineData("0")]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenStu3Server_WhenUpdatingAResourceWithInvalidETagHeader_TheServerShouldReturnAConflictResponse(string versionId)
        {
            Observation createdResource = await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.UpdateAsync(createdResource, versionId));

            Assert.Equal(System.Net.HttpStatusCode.Conflict, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenStu3Server_WhenUpdatingAResourceWithIncorrectETagHeader_TheServerShouldReturnAConflictResponse()
        {
            Observation createdResource = await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            // Specify a version that is one off from the version of the existing resource
            var incorrectVersionId = int.Parse(createdResource.Meta.VersionId) + 1;
            FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.UpdateAsync(createdResource, incorrectVersionId.ToString()));

            Assert.Equal(System.Net.HttpStatusCode.Conflict, ex.StatusCode);
        }
    }
}
