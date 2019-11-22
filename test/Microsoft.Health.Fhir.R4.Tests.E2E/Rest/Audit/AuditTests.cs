// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Audit
{
    /// <summary>
    /// Provides R4 specific tests.
    /// </summary>
    /// [Trait(Traits.Category, Categories.Batch)]
    public partial class AuditTests
    {
        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABatch_WhenPost_ThenAuditLogEntriesShouldBeCreated()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                // This test only works with the in-proc server with customized middleware pipeline
                return;
            }

            var expectedActions = new List<string> { "batch", "delete", "delete", "create", "create", "create", "create", "update", "update", "update", "update", "update", "update", "search-type", "search-type", "read", "read", "batch" };
            var expectedPathSegments = new List<string> { string.Empty, "Patient/234", "Patient/234", "Patient", "Patient", "Patient", "Patient", "Patient/123", "Patient/123", "Patient?identifier=http:/example.org/fhir/ids|456456", "Patient?identifier=http:/example.org/fhir/ids|456456", "Patient/123", "Patient/123", "Patient?name=peter", "Patient?name=peter", "Patient/12334", "Patient/12334", string.Empty };
            var expectedStatusCodes = new List<HttpStatusCode> { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.NoContent, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.PreconditionFailed, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.OK };
            await ExecuteAndValidateBatch(
               () => _client.PostBundleAsync(Samples.GetDefaultBatch().ToPoco()),
               expectedActions,
               expectedPathSegments,
               expectedStatusCodes);
        }
    }
}
