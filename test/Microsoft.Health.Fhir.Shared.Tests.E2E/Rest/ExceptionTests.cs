// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Hl7.Fhir.Model;
using Hl7.Fhir.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class ExceptionTests : IClassFixture<HttpIntegrationTestFixture<ExceptionTests.StartupWithThrowingMiddleware>>
    {
        private readonly HttpIntegrationTestFixture<StartupWithThrowingMiddleware> _fixture;

        public ExceptionTests(HttpIntegrationTestFixture<StartupWithThrowingMiddleware> fixture)
        {
            _fixture = fixture;
            Client = fixture.FhirClient;
        }

        protected FhirClient Client { get; set; }

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

            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Issue);
            Assert.Equal(OperationOutcome.IssueType.NotFound, operationOutcome.Issue[0].Code);
            Assert.Equal(OperationOutcome.IssueSeverity.Error, operationOutcome.Issue[0].Severity);
            TestHelper.AssertSecurityHeaders(fhirException.Headers);

            DotNetAttributeValidation.Validate(operationOutcome, true);
        }

        public class StartupWithThrowingMiddleware : Startup
        {
            public StartupWithThrowingMiddleware(IConfiguration configuration)
                : base(configuration)
            {
            }

            public override void Configure(IApplicationBuilder app)
            {
                app.Use(async (context, next) =>
                {
                    const string internalExceptionThrown = "internalExceptionThrown";

                    var throwValue = context.Request.Query["throw"];

                    switch (throwValue)
                    {
                        // Internal is used to cause the ExceptionHandlerMiddleware logic to execute
                        case "internal":
                            // Only throw the error the first time that this path is executed.
                            // This allows the ExceptionHandlerMiddleware to continue to the error page on the second execution of this path.
                            if (!context.Items.ContainsKey(internalExceptionThrown))
                            {
                                context.Items[internalExceptionThrown] = true;
                                throw new Exception("internal exception");
                            }

                            break;

                        // Middleware is used to cause the BaseExceptionMiddleware logic to execute
                        case "middleware":
                            throw new Exception("middleware exception");
                    }

                    await next();
                });

                base.Configure(app);
            }
        }
    }
}
