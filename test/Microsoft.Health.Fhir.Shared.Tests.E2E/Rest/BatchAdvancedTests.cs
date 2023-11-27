// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using static Hl7.Fhir.Model.OperationOutcome;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class BatchAdvancedTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;
        private readonly Dictionary<HttpStatusCode, string> _statusCodeMap = new Dictionary<HttpStatusCode, string>()
        {
            { HttpStatusCode.NoContent, "204" },
            { HttpStatusCode.NotFound, "404" },
            { HttpStatusCode.OK, "200" },
            { HttpStatusCode.PreconditionFailed, "412" },
            { HttpStatusCode.Created, "201" },
            { HttpStatusCode.Forbidden, "403" },
        };

        public BatchAdvancedTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAValidBundle_WhenSubmittingABatchTwiceWithAndWithoutChanges_ThenVersionIsCreatedWhenDataIsChanged()
        {
            // This test has a dependency to bundle sequential processing logic.
            FhirBundleProcessingLogic processingLogic = FhirBundleProcessingLogic.Sequential;

            var requestBundle = Samples.GetDefaultBatch().ToPoco<Bundle>();
            using FhirResponse<Bundle> fhirResponse = await _client.PostBundleAsync(
                requestBundle,
                processingLogic: processingLogic);

            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

            Bundle resource = fhirResponse.Resource;

            Assert.Equal("201", resource.Entry[0].Response.Status);
            Assert.Equal("201", resource.Entry[1].Response.Status);
            Assert.Equal("201", resource.Entry[2].Response.Status);
            Assert.Equal("1", resource.Entry[2].Resource.VersionId);
            Assert.Equal("201", resource.Entry[3].Response.Status);
            Assert.Equal(((int)Constants.IfMatchFailureStatus).ToString(), resource.Entry[4].Response.Status);
            Assert.Equal("204", resource.Entry[5].Response.Status);
            Assert.Equal("204", resource.Entry[6].Response.Status);
            BatchTestsUtil.ValidateOperationOutcome(resource.Entry[7].Response.Status, resource.Entry[7].Response.Outcome as OperationOutcome, _statusCodeMap[HttpStatusCode.NotFound], "The route for \"/ValueSet/$lookup\" was not found.", IssueType.NotFound);
            Assert.Equal("200", resource.Entry[8].Response.Status);
            BatchTestsUtil.ValidateOperationOutcome(resource.Entry[9].Response.Status, resource.Entry[9].Response.Outcome as OperationOutcome, _statusCodeMap[HttpStatusCode.NotFound], "Resource type 'Patient' with id '12334' couldn't be found.", IssueType.NotFound);

            // WhenSubmittingABatchTwiceWithNoDataChange_ThenServerShouldNotCreateAVersionSecondTimeAndSendOk
            using FhirResponse<Bundle> fhirResponseAfterPostingSameBundle = await _client.PostBundleAsync(
                requestBundle,
                processingLogic);

            Assert.NotNull(fhirResponseAfterPostingSameBundle);
            Assert.Equal(HttpStatusCode.OK, fhirResponseAfterPostingSameBundle.StatusCode);

            Bundle resourceAfterPostingSameBundle = fhirResponseAfterPostingSameBundle.Resource;

            Assert.Equal("201", resourceAfterPostingSameBundle.Entry[0].Response.Status);
            Assert.Equal("200", resourceAfterPostingSameBundle.Entry[1].Response.Status);
            Assert.Equal("200", resourceAfterPostingSameBundle.Entry[2].Response.Status);
            Assert.Equal(resource.Entry[2].Resource.VersionId, resourceAfterPostingSameBundle.Entry[2].Resource.VersionId);
            Assert.Equal("200", resourceAfterPostingSameBundle.Entry[3].Response.Status);
            Assert.Equal(resource.Entry[3].Resource.VersionId, resourceAfterPostingSameBundle.Entry[3].Resource.VersionId);
            Assert.Equal(((int)Constants.IfMatchFailureStatus).ToString(), resourceAfterPostingSameBundle.Entry[4].Response.Status);
            Assert.Equal("204", resourceAfterPostingSameBundle.Entry[5].Response.Status);
            Assert.Equal("204", resourceAfterPostingSameBundle.Entry[6].Response.Status);
            BatchTestsUtil.ValidateOperationOutcome(resourceAfterPostingSameBundle.Entry[7].Response.Status, resourceAfterPostingSameBundle.Entry[7].Response.Outcome as OperationOutcome, _statusCodeMap[HttpStatusCode.NotFound], "The route for \"/ValueSet/$lookup\" was not found.", IssueType.NotFound);
            Assert.Equal("200", resourceAfterPostingSameBundle.Entry[8].Response.Status);
            Assert.Equal(resource.Entry[8].Resource.VersionId, resourceAfterPostingSameBundle.Entry[8].Resource.VersionId);
            BatchTestsUtil.ValidateOperationOutcome(resourceAfterPostingSameBundle.Entry[9].Response.Status, resourceAfterPostingSameBundle.Entry[9].Response.Outcome as OperationOutcome, _statusCodeMap[HttpStatusCode.NotFound], "Resource type 'Patient' with id '12334' couldn't be found.", IssueType.NotFound);
        }
    }
}
