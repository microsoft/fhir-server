// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// STU3 requires different errors to be returned for resource versioning conflicts than R4 and R5.
    /// This test class is split up by FHIR version to accommodate this.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public partial class FhirStorageVersioningPolicyTests
    {
        [Fact]
        public async Task GivenAResourceTypeWithVersionedUpdateVersioningPolicy_WhenUpsertingWithoutSpecifyingVersion_ThenABadRequestExceptionIsThrown()
        {
            // The FHIR storage fixture configures medication resources to have the "versioned-update" versioning policy
            RawResourceElement medicationResource = await Mediator.CreateResourceAsync(Samples.GetDefaultMedication());

            ResourceElement newResourceValues = Samples.GetDefaultMedication().UpdateId(medicationResource.Id);

            // Do not pass in the eTag of the resource being updated
            // This simulates a request where the most recent version of the resource is not specified in the if-match header
            var exception = await Assert.ThrowsAsync<BadRequestException>(async () => await Mediator.UpsertResourceAsync(newResourceValues, weakETag: null));
            Assert.Equal(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, KnownResourceTypes.Medication), exception.Message);
        }

        [Fact]
        public async Task GivenAResourceTypeWithVersionedUpdateVersioningPolicy_WhenUpsertingWithNonMatchingVersion_ThenAPreconditionFailedExceptionIsThrown()
        {
            // The FHIR storage fixture configures medication resources to have the "versioned-update" versioning policy
            RawResourceElement medicationResource = await Mediator.CreateResourceAsync(Samples.GetDefaultMedication());

            ResourceElement newResourceValues = Samples.GetDefaultMedication().UpdateId(medicationResource.Id);

            // Pass in a version that does not match the most recent version of the resource being updated
            // This simulates a request where a non-matching version is specified in the if-match header
            const string incorrectVersion = "2";
            var exception = await Assert.ThrowsAsync<PreconditionFailedException>(async () => await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(incorrectVersion)));
            Assert.Equal(string.Format(Core.Resources.ResourceVersionConflict, incorrectVersion), exception.Message);
        }
    }
}
