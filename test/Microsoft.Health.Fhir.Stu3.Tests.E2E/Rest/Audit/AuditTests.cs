// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Audit
{
    /// <summary>
    /// Provides STU3 specific tests.
    /// </summary>
    [Trait(Traits.Category, Categories.Batch)]
    public partial class AuditTests
    {
        [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABatch_WhenPost_ThenAuditLogEntriesShouldBeCreated()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                // This test only works with the in-proc server with customized middleware pipeline
                return;
            }

            List<(string expectedActions, string expectedPathSegments, HttpStatusCode? expectedStatusCodes, ResourceType? resourceType)> expectedList = new List<(string, string, HttpStatusCode?, ResourceType?)>
            {
                ("batch", string.Empty, HttpStatusCode.OK, ResourceType.Bundle),
                ("delete", "Patient/234", HttpStatusCode.NoContent, null),
                ("create", "Patient", HttpStatusCode.Created, ResourceType.Patient),
                ("create", "Patient", HttpStatusCode.Created, ResourceType.Patient),
                ("update", "Patient/123", HttpStatusCode.OK, ResourceType.Patient),
                ("update", "Patient?identifier=234234", HttpStatusCode.OK, ResourceType.Patient),
                ("update", "Patient/123a", HttpStatusCode.NotFound, ResourceType.OperationOutcome),
                ("search-type", "Patient?name=peter", HttpStatusCode.OK, ResourceType.Bundle),
                ("read", "Patient/12334", HttpStatusCode.NotFound, ResourceType.OperationOutcome),
            };

            await ExecuteAndValidateBatch(
               () => _client.PostBundleAsync(Samples.GetDefaultBatch().ToPoco()),
               expectedList);
        }
    }
}
