// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All, FhirVersion.All)]
    public class ExceptionTests : IClassFixture<ExceptionTestFixture>
    {
        private readonly ExceptionTestFixture _fixture;

        public ExceptionTests(ExceptionTestFixture fixture)
        {
            _fixture = fixture;
            Client = fixture.FhirClient;
        }

        protected IVersionSpecificFhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttp_GivenAnInternalThrowQuerystring_TheServerShouldReturnAnOperationOutcome()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                // this test only works with the in-proc server with customized middleware pipeline
                return;
            }

            var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.ReadAsync("?throw=internal"));

            Assert.Equal(HttpStatusCode.InternalServerError, fhirException.StatusCode);

            var operationOutcome = fhirException.OperationOutcome;

            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Select("Resource.issue.children()"));
            Assert.Equal("exception", operationOutcome.Scalar<string>("Resource.issue.first().code"));
            Assert.Equal("error", operationOutcome.Scalar<string>("Resource.issue.first().severity"));
            TestHelper.AssertSecurityHeaders(fhirException.Headers);

            Client.Validate(operationOutcome);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttp_GivenAMiddlewareThrowQuerystring_TheServerShouldReturnAnOperationOutcome()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                // this test only works with the in-proc server with customized middleware pipeline
                return;
            }

            var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.ReadAsync("?throw=middleware"));

            Assert.Equal(HttpStatusCode.InternalServerError, fhirException.StatusCode);

            var operationOutcome = fhirException.OperationOutcome;

            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Select("Resource.issue.children()"));
            Assert.Equal("exception", operationOutcome.Scalar<string>("Resource.issue.first().code"));
            Assert.Equal("fatal", operationOutcome.Scalar<string>("Resource.issue.first().severity"));
            TestHelper.AssertSecurityHeaders(fhirException.Headers);

            Client.Validate(operationOutcome);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttp_GivenAnUnknownRoute_TheServerShouldReturnAnOperationOutcome()
        {
            var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.ReadAsync("unknownRoute"));

            Assert.Equal(HttpStatusCode.NotFound, fhirException.StatusCode);

            var operationOutcome = fhirException.OperationOutcome;

            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Select("Resource.issue.children()"));
            Assert.Equal("not-found", operationOutcome.Scalar<string>("Resource.issue.first().code"));
            Assert.Equal("error", operationOutcome.Scalar<string>("Resource.issue.first().severity"));
            TestHelper.AssertSecurityHeaders(fhirException.Headers);

            Client.Validate(operationOutcome);
        }
    }
}
