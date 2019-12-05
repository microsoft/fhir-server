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
    [Trait(Traits.Category, Categories.Batch)]
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

            List<(string expectedActions, string expectedPathSegments, HttpStatusCode expectedStatusCodes)> expectedList = new List<(string, string, HttpStatusCode)>
            {
                ("batch", string.Empty, HttpStatusCode.OK),
                ("delete", "Patient/234", HttpStatusCode.OK),
                ("delete", "Patient/234", HttpStatusCode.NoContent),
                ("create", "Patient", HttpStatusCode.OK),
                ("create", "Patient", HttpStatusCode.Created),
                ("create", "Patient", HttpStatusCode.OK),
                ("create", "Patient", HttpStatusCode.Created),
                ("update", "Patient/123", HttpStatusCode.OK),
                ("update", "Patient/123", HttpStatusCode.OK),
                ("update", "Patient?identifier=http:/example.org/fhir/ids|456456", HttpStatusCode.OK),
                ("update", "Patient?identifier=http:/example.org/fhir/ids|456456", HttpStatusCode.Created),
                ("update", "Patient/123", HttpStatusCode.OK),
                ("update", "Patient/123", HttpStatusCode.PreconditionFailed),
                ("search-type", "Patient?name=peter", HttpStatusCode.OK),
                ("search-type", "Patient?name=peter", HttpStatusCode.OK),
                ("read", "Patient/12334", HttpStatusCode.OK),
                ("read", "Patient/12334", HttpStatusCode.NotFound),
                ("batch", string.Empty, HttpStatusCode.OK),
            };

            await ExecuteAndValidateBatch(
               () => _client.PostBundleAsync(Samples.GetDefaultBatch().ToPoco()),
               expectedList);
        }
    }
}
