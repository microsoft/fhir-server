// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Search;
using Microsoft.Health.Test.Utilities;
using Microsoft.Net.Http.Headers;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.CompartmentSearch)]
    public class EverythingOperationTests : SearchTestsBase<EverythingOperationTestFixture>
    {
        public EverythingOperationTests(EverythingOperationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientEverythingOperationWithId_WhenSearched_ThenResourcesInScopeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything";

            FhirResponse<Bundle> firstBundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(firstBundle, Fixture.Patient, Fixture.Organization);

            var nextLink = firstBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> secondBundle = await Client.SearchAsync(nextLink);
#if R5
            ValidateBundle(secondBundle, Fixture.Observation, Fixture.Appointment, Fixture.Encounter, Fixture.Device);
#else
            ValidateBundle(secondBundle, Fixture.Observation, Fixture.Appointment, Fixture.Encounter);
            nextLink = secondBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> thirdBundle = await Client.SearchAsync(nextLink);
            ValidateBundle(thirdBundle, Fixture.Device);
#endif
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientEverythingOperationWithNonExistentId_WhenSearched_ThenResourcesInScopeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.NonExistentPatient.Id}/$everything";
#if R5
            await ExecuteAndValidateBundle(searchUrl, true, 2, Fixture.ObservationOfNonExistentPatient, Fixture.DeviceOfNonExistentPatient);
#else
            await ExecuteAndValidateBundle(searchUrl, true, 1, Fixture.ObservationOfNonExistentPatient, Fixture.DeviceOfNonExistentPatient);
#endif
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAEverythingOperationWithUnsupportedType_WhenSearched_ThenNotFoundShouldBeReturned()
        {
            string searchUrl = "Observation/bar/$everything";

            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync(searchUrl));

            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenTypeSpecified_WhenAllValid_ThenResourcesOfValidTypesShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=Patient,Observation";

            await ExecuteAndValidateBundle(searchUrl, true, 1, Fixture.Patient, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenTypeSpecified_WhenAllInvalid_ThenAnEmptyBundleShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=foo";

            await ExecuteAndValidateBundle(searchUrl);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenTypeSpecified_WhenSomeInvalid_ThenResourcesOfValidTypesShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=Patient,Device,foo";

            await ExecuteAndValidateBundle(searchUrl, true, 1, Fixture.Patient, Fixture.Device);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenStartOrEndSpecified_WhenSearched_ThenResourcesOfSpecifiedRangeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?end=2010";
#if R5
            await ExecuteAndValidateBundle(searchUrl, true, 2, Fixture.Patient, Fixture.Organization, Fixture.Appointment, Fixture.Device);
#else
            await ExecuteAndValidateBundle(searchUrl, true, 2, Fixture.Patient, Fixture.Organization, Fixture.Appointment);
#endif
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenSinceSpecified_WhenSearched_ThenResourcesOfSpecifiedRangeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_since=3000";

            await ExecuteAndValidateBundle(searchUrl);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("Zm9v")]
        [InlineData("eyJQaGFzZSI6MSwgIkludGVybmFsQ29udGludWF0aW9uVG9rZW4iOiAiWm05diJ9")]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenContinuationTokenSpecified_WhenInvalid_ThenBadRequestShouldBeReturned(string continuationToken)
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?ct={continuationToken}";

            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync(searchUrl));

            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMultipleInputParametersSpecified_WhenAllValid_ThenResourcesOfSpecifiedRangeShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=Patient,Observation,Encounter&start=2010&_since=2010";

            await ExecuteAndValidateBundle(searchUrl, true, 1, Fixture.Patient, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMultipleInputParametersSpecified_WhenAllInvalid_ThenInvalidInputParametersDoNotTakeEffect()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_count=100&foo=bar";

            string[] expectedDiagnostics = { string.Format(Api.Resources.UnsupportedParameter, "_count"), string.Format(Api.Resources.UnsupportedParameter, "foo") };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported, OperationOutcome.IssueType.NotSupported };
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning, OperationOutcome.IssueSeverity.Warning };

            Bundle bundle = await Client.SearchAsync(searchUrl);
            OperationOutcome outcome = GetAndValidateOperationOutcome(bundle);

            // Resources in scope are returned
            ValidateBundle(bundle, Fixture.Patient, Fixture.Organization, outcome);

            // OperationOutcome is added for unsupported parameters
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, outcome);

            // Unsupported parameters are removed from Bundle.link.url
            Assert.True(bundle.Link.All(x => !x.Url.Contains("_count", StringComparison.OrdinalIgnoreCase) && !x.Url.Contains("foo", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMultipleInputParametersSpecified_WhenSomeInvalid_ThenInvalidInputParametersDoNotTakeEffect()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/$everything?_type=Patient,Observation,Encounter&start=2010&_since=2010&foo=bar";

            string[] expectedDiagnostics = { string.Format(Api.Resources.UnsupportedParameter, "foo") };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported };
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning };

            FhirResponse<Bundle> firstBundle = await Client.SearchAsync(searchUrl);
            OperationOutcome firstOutcome = GetAndValidateOperationOutcome(firstBundle);

            // Resources in scope are returned
            ValidateBundle(firstBundle, Fixture.Patient, firstOutcome);

            // OperationOutcome is added for unsupported parameters
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, firstOutcome);

            // Unsupported parameters are removed from Bundle.link.url
            Assert.True(firstBundle.Resource.Link.All(x => !x.Url.Contains("foo", StringComparison.OrdinalIgnoreCase)));

            var nextLink = firstBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> secondBundle = await Client.SearchAsync(nextLink);
            ValidateBundle(secondBundle, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatientWithSeeAlsoLink_WhenRunningPatientEverything_ThenPatientEverythingShouldRunOnLink()
        {
            string searchUrl = $"Patient/{Fixture.PatientWithSeeAlsoLink.Id}/$everything";

            FhirResponse<Bundle> firstBundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(firstBundle, Fixture.PatientWithSeeAlsoLink);

            var nextLink = firstBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> secondBundle = await Client.SearchAsync(nextLink);
            ValidateBundle(secondBundle, Fixture.PatientReferencedBySeeAlsoLink);

            nextLink = secondBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> thirdBundle = await Client.SearchAsync(nextLink);
            ValidateBundle(thirdBundle, Fixture.ObservationOfPatientReferencedBySeeAlsoLink);

            nextLink = thirdBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> fourthBundle = await Client.SearchAsync(nextLink);
            Assert.Empty(fourthBundle.Resource.Entry);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatientWithSeeAlsoLinkRemovedMidOperation_WhenRunningPatientEverything_ThenPatientEverythingShouldRunOnLink()
        {
            string searchUrl = $"Patient/{Fixture.PatientWithSeeAlsoLinkToRemove.Id}/$everything";

            FhirResponse<Bundle> firstBundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(firstBundle, Fixture.PatientWithSeeAlsoLinkToRemove);

            // Remove the "seealso" link from the patient after the first $everything API call has been completed.
            Fixture.PatientWithSeeAlsoLinkToRemove.Link.Clear();
            await Fixture.UpdatePatient(Fixture.PatientWithSeeAlsoLinkToRemove);

            var nextLink = firstBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> secondBundle = await Client.SearchAsync(nextLink);
            ValidateBundle(secondBundle, Fixture.ObservationOfPatientWithSeeAlsoLinkToRemove);

            // Subsequent $everything API calls use the version of the patient passed in on the first API call.
            // The update to remove the "seealso" link is not recognized.
            nextLink = secondBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> thirdBundle = await Client.SearchAsync(nextLink);
            ValidateBundle(thirdBundle, Fixture.PatientReferencedByRemovedSeeAlsoLink);

            nextLink = thirdBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> fourthBundle = await Client.SearchAsync(nextLink);
            Assert.Empty(fourthBundle.Resource.Entry);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatientWithTwoSeeAlsoLinks_WhenRunningPatientEverything_ThenPatientEverythingShouldRunOnLinks()
        {
            string searchUrl = $"Patient/{Fixture.PatientWithTwoSeeAlsoLinks.Id}/$everything";

            FhirResponse<Bundle> firstBundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(firstBundle, Fixture.PatientWithTwoSeeAlsoLinks);

            // The "seealso" links will be processed in sorted order by id
            var orderedPatientsReferencedByLink = Fixture.PatientsReferencedBySeeAlsoLink.OrderBy(p => p.Id).ToList();

            var nextLink = firstBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> secondBundle = await Client.SearchAsync(nextLink);
            ValidateBundle(secondBundle, orderedPatientsReferencedByLink[0]);

            nextLink = secondBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> thirdBundle = await Client.SearchAsync(nextLink);
            ValidateBundle(thirdBundle, orderedPatientsReferencedByLink[1]);

            nextLink = thirdBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> fourthBundle = await Client.SearchAsync(nextLink);
            Assert.Empty(fourthBundle.Resource.Entry);
        }

        [Theory]
        [InlineData("handling=lenient")]
        [InlineData("")]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatientWithReplacedByLink_WhenRunningPatientEverythingWithPreferHeaderSetToLenientHandling_ThenMovedPermanentlyWarningShouldBeInBundle(string preferHeaderValue)
        {
            string[] expectedDiagnostics =
            {
                string.Format(CultureInfo.InvariantCulture, Core.Resources.EverythingOperationResourceIrrelevant, Fixture.PatientWithReplacedByLink.Id, Fixture.PatientReferencedByReplacedByLink.Id),
            };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.Conflict, };
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning };

            string searchUrl = $"Patient/{Fixture.PatientWithReplacedByLink.Id}/$everything";

            var preferHeader = string.IsNullOrEmpty(preferHeaderValue) ? null : Tuple.Create(KnownHeaders.Prefer, "handling=lenient");

            FhirResponse<Bundle> firstBundle = await Client.SearchAsync(searchUrl, preferHeader);

            // Confirm that the correct operation outcome is returned in the bundle
            OperationOutcome outcome = GetAndValidateOperationOutcome(firstBundle);
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, outcome);
            ValidateBundle(firstBundle, outcome, Fixture.PatientWithReplacedByLink);

            var nextLink = firstBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> secondBundle = await Client.SearchAsync(nextLink);

            Assert.Empty(secondBundle.Resource.Entry);
            Assert.Null(secondBundle.Resource.NextLink);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatientWithReplacedByLink_WhenRunningPatientEverythingWithPreferHeaderSetToStrictHandling_ThenMovedPermanentlyIsReturned()
        {
            string searchUrl = $"Patient/{Fixture.PatientWithReplacedByLink.Id}/$everything";

            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync(searchUrl, Tuple.Create(KnownHeaders.Prefer, "handling=strict")));

            // Confirm header location contains url for the correct request
            string id = Fixture.PatientReferencedByReplacedByLink.Id;
            string uri = $"/{KnownResourceTypes.Patient}/{id}/$everything";

            Assert.Contains(uri, ex.Content.Headers.GetValues(HeaderNames.ContentLocation).First());
            Assert.Equal(HttpStatusCode.MovedPermanently, ex.StatusCode);
            Assert.Contains(string.Format(Core.Resources.EverythingOperationResourceIrrelevant, Fixture.PatientWithReplacedByLink.Id, Fixture.PatientReferencedByReplacedByLink.Id), ex.Message);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatientWithReplacesLink_WhenRunningPatientEverything_ThenLinkShouldBeIgnored()
        {
            string searchUrl = $"Patient/{Fixture.PatientWithReplacesLink.Id}/$everything";

            FhirResponse<Bundle> firstBundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(firstBundle, Fixture.PatientWithReplacesLink);

            var nextLink = firstBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> secondBundle = await Client.SearchAsync(nextLink);

            Assert.Empty(secondBundle.Resource.Entry);
            Assert.Null(secondBundle.Resource.NextLink);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatientWithReferLink_WhenRunningPatientEverything_ThenLinkShouldBeIgnored()
        {
            string searchUrl = $"Patient/{Fixture.PatientWithReferLink.Id}/$everything";

            FhirResponse<Bundle> firstBundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(firstBundle, Fixture.PatientWithReferLink);

            var nextLink = firstBundle.Resource.NextLink.ToString();
            FhirResponse<Bundle> secondBundle = await Client.SearchAsync(nextLink);

            Assert.Empty(secondBundle.Resource.Entry);
            Assert.Null(secondBundle.Resource.NextLink);
        }
    }
}
