// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenUpdatingAResourceWithIncorrectETagHeader_GivenR4Server_TheServerShouldReturnAPreconditionFailedResponse()
        {
            Observation createdResource = await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.UpdateAsync(createdResource, Guid.NewGuid().ToString()));

            Assert.Equal(System.Net.HttpStatusCode.PreconditionFailed, ex.StatusCode);
        }
    }
}
