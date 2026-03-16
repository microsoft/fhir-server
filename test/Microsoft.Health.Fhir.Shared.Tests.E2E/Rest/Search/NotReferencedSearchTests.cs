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
using Hl7.Fhir.Support;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class NotReferencedSearchTests : SearchTestsBase<NotReferencedSearchTestFixture>
    {
        public NotReferencedSearchTests(NotReferencedSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task GivenANotReferencedWildcardSearchParameter_WhenSearched_ThenOnlyResourcesWithNoReferencesAreReturned()
        {
            var url = $"_not-referenced=*:*&_tag={Fixture.Tag}";

            var references = new List<string>()
            {
                Fixture.ObservationSubject.Subject.Reference,
                Fixture.ObservationPerformer.Performer[0].Reference,
                Fixture.Encounter.Subject.Reference,
            };

            await RunNotReferencedTest(url, references);
        }

        [Fact]
        public async Task GivenANotReferencedFieldWildcardSearchParameter_WhenSearched_ThenOnlyResourcesWithNoReferencesAreReturned()
        {
            var url = $"_not-referenced=Observation:*&_tag={Fixture.Tag}";

            var references = new List<string>()
            {
                Fixture.ObservationSubject.Subject.Reference,
                Fixture.ObservationPerformer.Performer[0].Reference,
            };

            await RunNotReferencedTest(url, references);
        }

        [Fact]
        public async Task GivenANotReferencedSpecificSearchParameter_WhenSearched_ThenOnlyResourcesWithNoReferencesAreReturned()
        {
            var url = $"_not-referenced=Observation:subject&_tag={Fixture.Tag}";

            var references = new List<string>()
            {
                Fixture.ObservationSubject.Subject.Reference,
            };

            await RunNotReferencedTest(url, references);
        }

        [Fact]
        public async Task GivenAnInvalidNotReferencedSearchParameter_WhenSearched_ThenItIsIgnoredAndAWarningIsReturned()
        {
            try
            {
                Bundle bundle = await Client.SearchAsync(ResourceType.Patient, $"_not-referenced=invalid&_tag={Fixture.Tag}");

                List<Resource> expected = ((Resource[])Fixture.Patients).ToList();

                var operationOutcome = new OperationOutcome();
                operationOutcome.AddIssue(new OperationOutcomeIssue(
                            OperationOutcomeConstants.IssueSeverity.Warning,
                            OperationOutcomeConstants.IssueType.NotSupported,
                            Core.Resources.NotReferencedParameterNoSeparator).ToPoco());

                expected.Add(operationOutcome);

                ValidateBundle(bundle, true, true, expected.ToArray());
            }
            catch (FhirClientException fce)
            {
                Assert.Fail($"A non-expected '{nameof(FhirClientException)}' was raised. Url: {Client.HttpClient.BaseAddress}. Activity Id: {fce.Response.GetRequestId()}. Error: {fce.Message}");
            }
            catch (Exception e)
            {
                Assert.Fail($"A non-expected '{e.GetType()}' was raised. Url: {Client.HttpClient.BaseAddress}. No Activity Id present. Error: {e.Message}");
            }
        }

        private async Task RunNotReferencedTest(string url, List<string> excludedReferences)
        {
            try
            {
                Bundle bundle = await Client.SearchAsync(ResourceType.Patient, url);

                Patient[] expected = Fixture.Patients.Where(patient => !excludedReferences.Any(reference => reference.Contains(patient.Id, StringComparison.OrdinalIgnoreCase))).ToArray();

                ValidateBundle(bundle, expected);
            }
            catch (FhirClientException fce)
            {
                Assert.Fail($"A non-expected '{nameof(FhirClientException)}' was raised. Url: {Client.HttpClient.BaseAddress}. Activity Id: {fce.Response.GetRequestId()}. Error: {fce.Message}");
            }
            catch (Exception e)
            {
                Assert.Fail($"A non-expected '{e.GetType()}' was raised. Url: {Client.HttpClient.BaseAddress}. No Activity Id present. Error: {e.Message}");
            }
        }
    }
}
