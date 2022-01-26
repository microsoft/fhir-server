// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]
    public class FhirStorageVersioningPolicyTests : IClassFixture<FhirStorageTestsFixture>
    {
        public FhirStorageVersioningPolicyTests(FhirStorageTestsFixture fixture)
        {
            Mediator = fixture?.Mediator;
        }

        protected Mediator Mediator { get; }

        [Fact]
        public async Task GivenAResourceTypeWithNoVersionVersioningPolicy_WhenSearchingHistory_ThenOnlyLatestVersionIsReturned()
        {
            // The FHIR storage fixture configures organization resources to have the "no-version" versioning policy
            SaveOutcome saveResult = await Mediator.UpsertResourceAsync(Samples.GetDefaultOrganization());

            ResourceElement newResourceValues = Samples.GetDefaultOrganization().UpdateId(saveResult.RawResourceElement.Id);

            SaveOutcome updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(saveResult.RawResourceElement.VersionId));

            ResourceElement historyResults = await Mediator.SearchResourceHistoryAsync(KnownResourceTypes.Organization, updateResult.RawResourceElement.Id);

            // The history bundle only has one entry because resource history is not kept
            Bundle bundle = historyResults.ToPoco<Bundle>();
            Assert.Single(bundle.Entry);

            Assert.Equal(WeakETag.FromVersionId(updateResult.RawResourceElement.VersionId).ToString(), bundle.Entry[0].Response.Etag);
        }

        [Fact]
        public async Task GivenAResourceTypeWithVersionedVersioningPolicy_WhenSearchingHistory_ThenAllVersionsAreReturned()
        {
            // The FHIR storage fixture configures observation resources to have the "versioned" versioning policy
            SaveOutcome saveResult = await Mediator.UpsertResourceAsync(Samples.GetDefaultObservation());

            ResourceElement newResourceValues = Samples.GetDefaultObservation().UpdateId(saveResult.RawResourceElement.Id);

            SaveOutcome updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(saveResult.RawResourceElement.VersionId));

            ResourceElement historyResults = await Mediator.SearchResourceHistoryAsync(KnownResourceTypes.Observation, updateResult.RawResourceElement.Id);

            // The history bundle has both versions because history is kept
            Bundle bundle = historyResults.ToPoco<Bundle>();
            Assert.Equal(2, bundle.Entry.Count);

            Assert.Equal(WeakETag.FromVersionId(updateResult.RawResourceElement.VersionId).ToString(), bundle.Entry.Max(entry => entry.Response.Etag));
            Assert.Equal(WeakETag.FromVersionId(saveResult.RawResourceElement.VersionId).ToString(), bundle.Entry.Min(entry => entry.Response.Etag));
        }

        [Fact]
        public async Task GivenAResourceTypeWithVersionedUpdateVersioningPolicy_WhenSearchingHistory_ThenAllVersionsAreReturned()
        {
            // The FHIR storage fixture configures medication resources to have the "versioned-update" versioning policy
            RawResourceElement medicationResource = await Mediator.CreateResourceAsync(Samples.GetDefaultMedication());

            ResourceElement newResourceValues = Samples.GetDefaultMedication().UpdateId(medicationResource.Id);

            SaveOutcome updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(medicationResource.VersionId));

            ResourceElement historyResults = await Mediator.SearchResourceHistoryAsync(KnownResourceTypes.Medication, updateResult.RawResourceElement.Id);

            // The history bundle has both versions because history is kept
            Bundle bundle = historyResults.ToPoco<Bundle>();
            Assert.Equal(2, bundle.Entry.Count);

            Assert.Equal(WeakETag.FromVersionId(updateResult.RawResourceElement.VersionId).ToString(), bundle.Entry.Max(entry => entry.Response.Etag));
            Assert.Equal(WeakETag.FromVersionId(medicationResource.VersionId).ToString(), bundle.Entry.Min(entry => entry.Response.Etag));
        }

        [Fact]
        public async Task GivenAResourceTypeWithVersionedUpdateVersioningPolicy_WhenUpsertingWithoutSpecifyingVersion_ThenAPreconditionFailedExceptionIsThrown()
        {
            // The FHIR storage fixture configures medication resources to have the "versioned-update" versioning policy
            RawResourceElement medicationResource = await Mediator.CreateResourceAsync(Samples.GetDefaultMedication());

            ResourceElement newResourceValues = Samples.GetDefaultMedication().UpdateId(medicationResource.Id);

            // Do not pass in the eTag of the resource being updated
            // This simulates a request where the most recent version of the resource is not specified in the if-match header
            await Assert.ThrowsAsync<PreconditionFailedException>(async () => await Mediator.UpsertResourceAsync(newResourceValues, weakETag: null));
        }
    }
}
