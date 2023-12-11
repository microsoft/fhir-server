// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.SearchParameterState;
using Microsoft.Health.Fhir.Shared.Tests.E2E.Rest.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Hl7.Fhir.Model.OperationOutcome;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.SelectableSearchParameters)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class IncludeSearchSelectableSearchParametersTests : SearchTestsBase<IncludeSearchTestFixture>
    {
        private static readonly Regex ContinuationTokenRegex = new Regex("&ct=");

        public IncludeSearchSelectableSearchParametersTests(IncludeSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task GivenAnRevIncludeSearchExpressionWithDisabledSearchParameter_WhenSearched_DoesnotIncludeDisabledSearchParameter()
        {
            string query = $"_revinclude:iterate=MedicationRequest:*&_revinclude=Patient:general-practitioner&_tag={Fixture.Tag}";

            await UpdateSearchParameterStatusAsync("http://hl7.org/fhir/SearchParameter/Patient-organization", SearchParameterStatus.Disabled, default);

            await SearchAndValidateBundleAsync(
                ResourceType.Patient,
                query,
                Fixture.PatiPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AdamsPatient,
                Fixture.PatientWithDeletedOrganization);

            await UpdateSearchParameterStatusAsync("http://hl7.org/fhir/SearchParameter/Patient-organization", SearchParameterStatus.Enabled, default);
        }

        // This will not work for circular reference
        private static void ValidateSearchEntryMode(Bundle bundle, ResourceType matchResourceType)
        {
            foreach (Bundle.EntryComponent entry in bundle.Entry)
            {
                var searchEntryMode = entry.Resource.TypeName == matchResourceType.ToString() ? Bundle.SearchEntryMode.Match : Bundle.SearchEntryMode.Include;
                Assert.Equal(searchEntryMode, entry.Search.Mode);
            }
        }

        // This should be used with circular references
        private static void ValidateSearchEntryMode(Bundle bundle, IDictionary<string, Bundle.SearchEntryMode> expectedSearchEntryModes)
        {
            foreach (Bundle.EntryComponent entry in bundle.Entry)
            {
                Assert.Equal(expectedSearchEntryModes[entry.Resource.Id], entry.Search.Mode);
            }
        }

        private async Task<Bundle> SearchAndValidateBundleAsync(ResourceType resourceType, string query, params Resource[] expectedResources)
        {
            Bundle bundle = null;
            try
            {
                bundle = await Client.SearchAsync(resourceType, query);
            }
            catch (FhirClientException fce)
            {
                Assert.Fail($"A non-expected '{nameof(FhirClientException)}' was raised. Url: {Client.HttpClient.BaseAddress}. Activity Id: {fce.Response.GetRequestId()}. Error: {fce.Message}");
            }

            Assert.True(bundle != null, "The bundle is null. This is a non-expected scenario for this test. Review the existing test code and flow.");

            ValidateBundle(bundle, expectedResources);

            ValidateSearchEntryMode(bundle, resourceType);

            string bundleUrl = bundle.Link[0].Url;
            MatchCollection matches = ContinuationTokenRegex.Matches(bundleUrl);
            if (!matches.Any())
            {
                ValidateBundleUrl(Client.HttpClient.BaseAddress, resourceType, query, bundleUrl);
            }
            else
            {
                int matchIndex = matches.First().Index;
                string bundleUriWithNoContinuationToken = bundleUrl.Substring(0, matchIndex);
                ValidateBundleUrl(Client.HttpClient.BaseAddress, resourceType, query, bundleUriWithNoContinuationToken);
            }

            return bundle;
        }

        private async Task UpdateSearchParameterStatusAsync(string searchParameterUri, SearchParameterStatus status, CancellationToken cancellationToken = default)
        {
            await Client.UpdateSearchParameterStateAsync(searchParameterUri, status, cancellationToken);
        }
    }
}
