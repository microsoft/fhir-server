// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Hl7.Fhir.Model;
using Hl7.Fhir.Validation;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public abstract class ExceptionTests<TFixture> : IClassFixture<TFixture>
        where TFixture : HttpIntegrationTestFixture<StartupWithThrowingMiddleware>
    {
        private readonly TFixture _fixture;

        protected ExceptionTests(TFixture fixture)
        {
            _fixture = fixture;
            Client = fixture.FhirClient;
        }

        protected FhirClient Client { get; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttp_GivenAnInternalThrowQuerystring_TheServerShouldReturnAnOperationOutcome()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                // this test only works with the in-proc server with customized middleware pipeline
                return;
            }

            var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.ReadAsync<OperationOutcome>("?throw=internal"));

            Assert.Equal(HttpStatusCode.InternalServerError, fhirException.StatusCode);

            var operationOutcome = fhirException.OperationOutcome;

            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Issue);
            Assert.Equal(OperationOutcome.IssueType.Exception, operationOutcome.Issue[0].Code);
            Assert.Equal(OperationOutcome.IssueSeverity.Error, operationOutcome.Issue[0].Severity);
            TestHelper.AssertSecurityHeaders(fhirException.Headers);

            DotNetAttributeValidation.Validate(operationOutcome, true);
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

            var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.ReadAsync<OperationOutcome>("?throw=middleware"));

            Assert.Equal(HttpStatusCode.InternalServerError, fhirException.StatusCode);

            var operationOutcome = fhirException.OperationOutcome;

            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Issue);
            Assert.Equal(OperationOutcome.IssueType.Exception, operationOutcome.Issue[0].Code);
            Assert.Equal(OperationOutcome.IssueSeverity.Fatal, operationOutcome.Issue[0].Severity);
            TestHelper.AssertSecurityHeaders(fhirException.Headers);

            DotNetAttributeValidation.Validate(operationOutcome, true);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttp_GivenAnUnknownRoute_TheServerShouldReturnAnOperationOutcome()
        {
            var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.ReadAsync<OperationOutcome>("unknownRoute"));

            Assert.Equal(HttpStatusCode.NotFound, fhirException.StatusCode);

            var operationOutcome = fhirException.OperationOutcome;

            Assert.NotNull(operationOutcome);
            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Issue);
            Assert.Equal(OperationOutcome.IssueType.NotFound, operationOutcome.Issue[0].Code);
            Assert.Equal(OperationOutcome.IssueSeverity.Error, operationOutcome.Issue[0].Severity);
            TestHelper.AssertSecurityHeaders(fhirException.Headers);

            DotNetAttributeValidation.Validate(operationOutcome, true);
        }
    }
}
