// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
    /// <summary>
    /// STU3 requires different errors to be returned for resource versioning conflicts than R4 and R5.
    /// This test class is split up by FHIR version to accommodate this.
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]
    public partial class FhirStorageVersioningPolicyTests : IClassFixture<FhirStorageTestsFixture>
    {
        private const string ContentUpdated = "Updated resource content";

        public FhirStorageVersioningPolicyTests(FhirStorageTestsFixture fixture)
        {
            Mediator = fixture?.Mediator;
        }

        protected Mediator Mediator { get; }

        [Fact]
        public async Task GivenAResourceTypeWithNoVersionVersioningPolicy_WhenSearchingHistory_ThenOnlyLatestVersionIsReturned()
        {
            // The FHIR storage fixture configures organization resources to have the "no-version" versioning policy
            RawResourceElement organizationResource = await Mediator.CreateResourceAsync(Samples.GetDefaultOrganization());
            Assert.Equal("1", organizationResource.VersionId);

            ResourceElement newResourceValues = Samples.GetDefaultOrganization().UpdateId(organizationResource.Id);
            //// next line is a must to make test valid, otherwise we do not attempt to save resource
            newResourceValues.ToPoco<Organization>().Text = new Narrative { Status = Narrative.NarrativeStatus.Generated, Div = $"<div>{ContentUpdated}</div>" };

            SaveOutcome updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(organizationResource.VersionId));
            Assert.Equal("2", updateResult.RawResourceElement.VersionId);

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
            RawResourceElement observationResource = await Mediator.CreateResourceAsync(Samples.GetDefaultObservation());

            ResourceElement newResourceValues = Samples.GetDefaultObservation().UpdateId(observationResource.Id);

            newResourceValues.ToPoco<Observation>().Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = $"<div>{ContentUpdated}</div>",
            };
            SaveOutcome updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(observationResource.VersionId));

            ResourceElement historyResults = await Mediator.SearchResourceHistoryAsync(KnownResourceTypes.Observation, updateResult.RawResourceElement.Id);

            // The history bundle has both versions because history is kept
            Bundle bundle = historyResults.ToPoco<Bundle>();
            Assert.Equal(2, bundle.Entry.Count);

            Assert.Equal(WeakETag.FromVersionId(updateResult.RawResourceElement.VersionId).ToString(), bundle.Entry.Max(entry => entry.Response.Etag));
            Assert.Equal(WeakETag.FromVersionId(observationResource.VersionId).ToString(), bundle.Entry.Min(entry => entry.Response.Etag));
        }

        [Fact]
        public async Task GivenAResourceTypeWithVersionedUpdateVersioningPolicy_WhenSearchingHistory_ThenAllVersionsAreReturned()
        {
            // The FHIR storage fixture configures medication resources to have the "versioned-update" versioning policy
            RawResourceElement medicationResource = await Mediator.CreateResourceAsync(Samples.GetDefaultMedication());

            ResourceElement newResourceValues = Samples.GetDefaultMedication().UpdateId(medicationResource.Id);

            newResourceValues.ToPoco<Medication>().Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = $"<div>{ContentUpdated}</div>",
            };
            SaveOutcome updateResult = await Mediator.UpsertResourceAsync(newResourceValues, WeakETag.FromVersionId(medicationResource.VersionId));

            ResourceElement historyResults = await Mediator.SearchResourceHistoryAsync(KnownResourceTypes.Medication, updateResult.RawResourceElement.Id);

            // The history bundle has both versions because history is kept
            Bundle bundle = historyResults.ToPoco<Bundle>();
            Assert.Equal(2, bundle.Entry.Count);

            Assert.Equal(WeakETag.FromVersionId(updateResult.RawResourceElement.VersionId).ToString(), bundle.Entry.Max(entry => entry.Response.Etag));
            Assert.Equal(WeakETag.FromVersionId(medicationResource.VersionId).ToString(), bundle.Entry.Min(entry => entry.Response.Etag));
        }

        [Fact]
        public async Task GivenAResourceTypeWithVersionedUpdateVersioningPolicy_WhenPutCreatingWithNoVersion_ThenResourceIsCreatedSuccessfully()
        {
            // The FHIR storage fixture configures medication resources to have the "versioned-update" versioning policy
            var randomId = Guid.NewGuid().ToString();

            // Upserting a resource that does not already exist in the database simulates a PUT create
            // Do not pass in the eTag to mock a request where no if-match header is provided
            await Mediator.UpsertResourceAsync(Samples.GetDefaultMedication().UpdateId(randomId), weakETag: null);

            // Confirm the resource is successfully created and has the id specified on creation
            RawResourceElement medicationSearchResult = await Mediator.GetResourceAsync(new ResourceKey<Medication>(randomId));
            Assert.Equal(randomId, medicationSearchResult.Id);
        }

        [Fact]
        public async Task GivenAResourceTypeWithVersionedUpdateVersioningPolicy_WhenPutCreatingWithAVersion_ThenAResourceNotFoundExceptionIsThrown()
        {
            // The FHIR storage fixture configures medication resources to have the "versioned-update" versioning policy
            var randomId = Guid.NewGuid().ToString();

            // Any version id on a PUT create is invalid, as we can't specify the version of a resource that does not exist
            const string invalidVersionId = "1";

            // Upserting a resource that does not already exist in the database simulates a PUT create
            // Pass in an eTag to mock a request where an invalid if-match header is provided
            var exception = await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await Mediator.UpsertResourceAsync(Samples.GetDefaultMedication().UpdateId(randomId), WeakETag.FromVersionId(invalidVersionId)));
            Assert.Equal(string.Format(Core.Resources.ResourceNotFoundByIdAndVersion, KnownResourceTypes.Medication, randomId, invalidVersionId), exception.Message);
        }
    }
}
