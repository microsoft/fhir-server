// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;
using Microsoft.Health.Fhir.Shared.Tests.E2E.Rest;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.BulkImport)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class BulkImportTests : IClassFixture<BulkImportTestFixture>
    {
        private const string ForbiddenMessage = "Forbidden: Authorization failed.";
        private readonly TestFhirClient _client;
        private readonly bool _isUsingInProcTestServer;

        public BulkImportTests(BulkImportTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
            _isUsingInProcTestServer = fixture.IsUsingInProcTestServer;
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

        [Fact]
        public async Task CreateASuccessTask_WhenBulkImport_CorrectResponseShouldReturn()
        {
            if (!_isUsingInProcTestServer)
            {
                // This test only works with the in-proc server with a customized task factory.
                return;
            }

            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.BulkImportUser, TestApplications.NativeClient);

            var request = Samples.GetDefaultBulkImportRequest();
            request.InputSource = new Uri("http://successTask");
            Uri contentLocation = await tempClient.BulkImportAsync(request);
            Assert.False(string.IsNullOrWhiteSpace(contentLocation.ToString()));
            await Task.Delay(5000);
            await tempClient.CheckBulkImportAsync(contentLocation);
        }

        [Fact]
        public async Task CreateAFailueTask_WhenBulkImport_ExceptionShouldBeThrown()
        {
            if (!_isUsingInProcTestServer)
            {
                // This test only works with the in-proc server with a customized task factory.
                return;
            }

            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.BulkImportUser, TestApplications.NativeClient);

            var request = Samples.GetDefaultBulkImportRequest();
            request.InputSource = new Uri("http://failueTask");
            Uri contentLocation = await tempClient.BulkImportAsync(request);
            Assert.False(string.IsNullOrWhiteSpace(contentLocation.ToString()));

            await Task.Delay(5000);
            await Assert.ThrowsAsync<FhirException>(async () => await tempClient.CheckBulkImportAsync(contentLocation));
        }
    }
}
