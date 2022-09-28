// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Web;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Extensions;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class CanonicalSearchTests : SearchTestsBase<CanonicalSearchTestFixture>
    {
        private const string _skipReason = "_profile was not supported as a search parameter.";

        public CanonicalSearchTests(CanonicalSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [SkippableFact]
        public async Task GivenAnObservationWithProfile_WhenSearchingByCanonicalUriVersionFragment_Then1ExpectedResultIsFound()
        {
            Skip.IfNot(Fixture.TestFhirServer.Metadata.SupportsSearchParameter("Observation", "_profile"), _skipReason);

            // We must encode '#' in the url or ASP.NET won't interpret this as part of the query string
            FhirResponse<Bundle> result = await Fixture.TestFhirClient.SearchAsync($"Observation?_profile={HttpUtility.UrlEncode(Fixture.ObservationProfileV1)}");

            if (ModelInfoProvider.Version == FhirSpecification.Stu3)
            {
                Assert.Collection(result.Resource.Entry, x => Assert.Equal(Fixture.ObservationProfileV1, x.Resource.Meta.Profile.Single()));
            }
            else
            {
                // Canonical components stripped from indexing (Canonical => Uri)
                Assert.Empty(result.Resource.Entry);
            }
        }

        [SkippableFact]
        public async Task GivenAnObservationWithProfile_WhenSearchingByCanonicalUri_ThenExpectedResultsAreFound()
        {
            Skip.IfNot(Fixture.TestFhirServer.Metadata.SupportsSearchParameter("Observation", "_profile"), _skipReason);

            FhirResponse<Bundle> result = await Fixture.TestFhirClient.SearchAsync($"Observation?_profile={Fixture.ObservationProfileUri}");

            if (ModelInfoProvider.Version == FhirSpecification.Stu3)
            {
                // No exact match
                Assert.Empty(result.Resource.Entry);
            }
            else
            {
                // Canonical components stripped from indexing and in search request
                Assert.Collection(
                    result.Resource.Entry,
                    x => Assert.Equal(Fixture.ObservationProfileV1, x.Resource.Meta.Profile.First()),
                    x => Assert.Equal(Fixture.ObservationProfileV2, x.Resource.Meta.Profile.First()),
                    x => Assert.Equal(Fixture.ObservationProfileUriAlternate, x.Resource.Meta.Profile.First()));
            }
        }

        [SkippableFact]
        public async Task GivenAnObservationWithProfile_WhenSearchingByCanonicalUriVersion_Then1ExpectedResultIsFound()
        {
            Skip.IfNot(Fixture.TestFhirServer.Metadata.SupportsSearchParameter("Observation", "_profile"), _skipReason);

            FhirResponse<Bundle> result = await Fixture.TestFhirClient.SearchAsync($"Observation?_profile={Fixture.ObservationProfileUri}|2");

            if (ModelInfoProvider.Version == FhirSpecification.Stu3)
            {
                // Stu3 gets an exact match
                Assert.Collection(
                    result.Resource.Entry,
                    x => Assert.Equal(Fixture.ObservationProfileV2, x.Resource.Meta.Profile.Single()));
            }
            else
            {
                // Canonical components stripped from indexing
                Assert.Empty(result.Resource.Entry);
            }
        }

        [SkippableFact]
        public async Task GivenAnObservationWithProfile_WhenSearchingByCanonicalUriMultipleProfiles_Then1ExpectedResultIsFound()
        {
            Skip.IfNot(Fixture.TestFhirServer.Metadata.SupportsSearchParameter("Observation", "_profile"), _skipReason);

            FhirResponse<Bundle> result = await Fixture.TestFhirClient.SearchAsync($"Observation?_profile={Fixture.ObservationProfileUriAlternate}");

            Assert.Collection(
                result.Resource.Entry,
                x =>
                {
                    Assert.Equal(Fixture.ObservationProfileUriAlternate, x.Resource.Meta.Profile.First());
                    Assert.Equal($"{Fixture.ObservationProfileUri}{Fixture.ObservationProfileV1Version}", x.Resource.Meta.Profile.Last());
                });
        }
    }
}
