// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.BulkImport)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class BulkImportTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private const string ForbiddenMessage = "Forbidden: Authorization failed.";
        private readonly TestFhirClient _client;

        public BulkImportTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [SkippableFact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAUserWithNoImportPermissions_WhenBulkImport_TheServerShouldReturnForbidden()
        {
            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.ReadOnlyUser, TestApplications.NativeClient);

            var request = Samples.GetDefaultBulkImportRequest();
            FhirException fhirException = await Assert.ThrowsAsync<FhirException>(async () => await tempClient.BulkImportAsync(request));
            Assert.Equal(ForbiddenMessage, fhirException.Message);
            Assert.Equal(HttpStatusCode.Forbidden, fhirException.StatusCode);
        }

        [SkippableFact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAUserWithImportPermissions_WhenBulkImport_TheServerShouldReturnSuccess()
        {
            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.BulkImportUser, TestApplications.NativeClient);

            var request = Samples.GetDefaultBulkImportRequest();
            Uri contentLocation = await tempClient.BulkImportAsync(request);
            await tempClient.CancelBulkImport(contentLocation);
        }
    }
}
