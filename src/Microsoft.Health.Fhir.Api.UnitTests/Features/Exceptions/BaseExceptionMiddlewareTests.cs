// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Exceptions
{
    public class BaseExceptionMiddlewareTests
    {
        private readonly string _correlationId;
        private readonly DefaultHttpContext _context;
        private readonly IFhirContextAccessor _fhirContextAccessor;
        private readonly FhirJsonSerializer _fhirJsonSerializer;
        private readonly FhirXmlSerializer _fhirXmlSerializer;
        private readonly CorrelationIdProvider _provider = () => Guid.NewGuid().ToString();

        public BaseExceptionMiddlewareTests()
        {
            _correlationId = Guid.NewGuid().ToString();

            _fhirContextAccessor = Substitute.For<IFhirContextAccessor>();
            _fhirContextAccessor.FhirContext.CorrelationId.Returns(_correlationId);
            _fhirJsonSerializer = new FhirJsonSerializer();
            _fhirXmlSerializer = new FhirXmlSerializer();

            _context = new DefaultHttpContext();

            // The default context has a null stream, so give it a memory stream instead
            _context.Response.Body = new MemoryStream();
        }

        [Theory]
        [InlineData("Test exception", "There was an error processing your request.")]
        [InlineData("IDX10803: Unable to obtain configuration from:", "Unable to obtain OpenID configuration.")]
        [InlineData("The MetadataAddress or Authority must use HTTPS unless disabled for development by setting RequireHttpsMetadata=false.", "The security configuration requires the authority to be set to an https address.")]
        public async Task WhenExecutingBaseExceptionMiddleware_GivenAnHttpContextWithException_TheResponseShouldBeOperationOutcome(string exceptionMessage, string diagnosticMessage)
        {
            var baseExceptionMiddleware = new BaseExceptionMiddleware(innerHttpContext => throw new Exception(exceptionMessage), NullLogger<BaseExceptionMiddleware>.Instance, _fhirContextAccessor, _fhirJsonSerializer, _fhirXmlSerializer, _provider);

            await baseExceptionMiddleware.Invoke(_context);

            Assert.Equal(500, _context.Response.StatusCode);
            Assert.Equal("application/fhir+json", _context.Response.ContentType);

            // Reset the response body stream to position 0 before using it
            _context.Response.Body.Position = 0;

            using (var bodyReader = new StreamReader(_context.Response.Body))
            {
                var responseBody = bodyReader.ReadToEndAsync();

                FhirJsonParser parser = new FhirJsonParser();
                var operationOutcome = parser.Parse<OperationOutcome>(await responseBody);

                Assert.NotEmpty(operationOutcome.Issue);
                Assert.Equal(_correlationId, operationOutcome.Id);
                Assert.Equal(diagnosticMessage, operationOutcome.Issue[0].Diagnostics);
                Assert.Equal(OperationOutcome.IssueSeverity.Fatal, operationOutcome.Issue[0].Severity);
                Assert.Equal(OperationOutcome.IssueType.Exception, operationOutcome.Issue[0].Code);
            }
        }

        [Fact]
        public async Task WhenExecutingBaseExceptionMiddleware_GivenAnHttpContextWithNoException_TheResponseShouldBeEmpty()
        {
            var baseExceptionMiddleware = new BaseExceptionMiddleware(innerHttpContext => Task.CompletedTask, NullLogger<BaseExceptionMiddleware>.Instance, _fhirContextAccessor, _fhirJsonSerializer, _fhirXmlSerializer, _provider);

            await baseExceptionMiddleware.Invoke(_context);

            Assert.Equal(200, _context.Response.StatusCode);
            Assert.Null(_context.Response.ContentType);
            Assert.Equal(0, _context.Response.Body.Length);
        }
    }
}
