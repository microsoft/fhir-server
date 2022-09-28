// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Exceptions
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class BaseExceptionMiddlewareTests
    {
        private readonly string _correlationId;
        private readonly DefaultHttpContext _context;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IFormatParametersValidator _formatParametersValidator;

        public BaseExceptionMiddlewareTests()
        {
            _correlationId = Guid.NewGuid().ToString();

            _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            _fhirRequestContextAccessor.RequestContext.CorrelationId.Returns(_correlationId);
            _formatParametersValidator = Substitute.For<IFormatParametersValidator>();

            _context = new DefaultHttpContext();

            // The default context has a null stream, so give it a memory stream instead
            _context.Response.Body = new MemoryStream();
        }

        [Theory]
        [InlineData("Test exception", "There was an error processing your request.")]
        [InlineData("IDX10803: Unable to obtain configuration from:", "Unable to obtain OpenID configuration.")]
        [InlineData("The MetadataAddress or Authority must use HTTPS unless disabled for development by setting RequireHttpsMetadata=false.", "The security configuration requires the authority to be set to an https address.")]
        public async Task GivenAnHttpContextWithException_WhenExecutingBaseExceptionMiddleware_TheResponseShouldBeOperationOutcome(string exceptionMessage, string diagnosticMessage)
        {
            var baseExceptionMiddleware = CreateBaseExceptionMiddleware(innerHttpContext => throw new Exception(exceptionMessage));

            baseExceptionMiddleware.ExecuteResultAsync(Arg.Any<HttpContext>(), Arg.Any<IActionResult>()).Returns(Task.CompletedTask);

            await baseExceptionMiddleware.Invoke(_context);

            await baseExceptionMiddleware
                .Received()
                .ExecuteResultAsync(
                    Arg.Any<HttpContext>(),
                    Arg.Is<OperationOutcomeResult>(x => x.StatusCode == HttpStatusCode.InternalServerError &&
                                            x.Result.Id == _correlationId &&
                                            x.Result.Issue[0].Diagnostics == diagnosticMessage));
        }

        [Fact]
        public async Task GivenAnHttpContextWithNoException_WhenExecutingBaseExceptionMiddleware_TheResponseShouldBeEmpty()
        {
            var baseExceptionMiddleware = CreateBaseExceptionMiddleware(innerHttpContext => Task.CompletedTask);

            await baseExceptionMiddleware.Invoke(_context);

            Assert.Equal(200, _context.Response.StatusCode);
            Assert.Null(_context.Response.ContentType);
            Assert.Equal(0, _context.Response.Body.Length);
        }

        private BaseExceptionMiddleware CreateBaseExceptionMiddleware(RequestDelegate nextDelegate)
        {
            return Substitute.ForPartsOf<BaseExceptionMiddleware>(nextDelegate, NullLogger<BaseExceptionMiddleware>.Instance, _fhirRequestContextAccessor, _formatParametersValidator);
        }
    }
}
