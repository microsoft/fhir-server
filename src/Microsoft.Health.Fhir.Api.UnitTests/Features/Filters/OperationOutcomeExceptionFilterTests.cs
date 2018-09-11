// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    public class OperationOutcomeExceptionFilterTests
    {
        private readonly ActionExecutedContext _context;
        private IFhirContextAccessor _fhirContextAccessor = Substitute.For<IFhirContextAccessor>();
        private IFhirContext _fhirContext = Substitute.For<IFhirContext>();
        private string _correlationId = Guid.NewGuid().ToString();

        public OperationOutcomeExceptionFilterTests()
        {
            _context = new ActionExecutedContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                FilterTestsHelper.CreateMockFhirController());

            _fhirContext.CorrelationId.Returns(_correlationId);
            _fhirContextAccessor.FhirContext.Returns(_fhirContext);
        }

        [Fact]
        public void GivenAFhirBasedException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            var filter = new OperationOutcomeExceptionFilterAttribute(_fhirContextAccessor);

            _context.Exception = Substitute.For<FhirException>();

            filter.OnActionExecuted(_context);

            var result = _context.Result as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(_correlationId, result.Resource.Id);
        }

        [Fact]
        public void GivenAResourceGoneExceptionException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            var filter = new OperationOutcomeExceptionFilterAttribute(_fhirContextAccessor);

            _context.Exception = new ResourceGoneException(new ResourceKey<Observation>("id1", "version2"));

            filter.OnActionExecuted(_context);

            var result = _context.Result as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.Gone, result.StatusCode.GetValueOrDefault());
            Assert.Equal("W/\"version2\"", result.Headers[HeaderNames.ETag]);

            var operation = result.Resource;
            Assert.NotNull(operation);
            Assert.Equal(_correlationId, result.Resource.Id);
        }

        [Fact]
        public void GivenAResourceNotFoundExceptionException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new ResourceNotFoundException("Exception"), HttpStatusCode.NotFound);
        }

        [Fact]
        public void GivenAFhirBasedExceptionWithIssueMessage_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcomeWithIssueComponent()
        {
            var reason = "This is a test reason.";

            OperationOutcome operation = ValidateOperationOutcome(
                Substitute.For<FhirException>(new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Error,
                    Code = OperationOutcome.IssueType.Invalid,
                    Diagnostics = reason,
                }),
                HttpStatusCode.BadRequest);

            Assert.Equal(reason, operation.Issue[0].Diagnostics);
        }

        [Fact]
        public void GivenAResourceNotValidExceptionException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            var propertyName = "test";
            var reason = "This is a test reason.";

            OperationOutcome operation = ValidateOperationOutcome(
                new ResourceNotValidException(new List<ValidationFailure>
                {
                    new ValidationFailure(propertyName, reason),
                }),
                HttpStatusCode.BadRequest);

            Assert.Equal(reason, operation.Issue[0].Diagnostics);
        }

        [Fact]
        public void GivenAnUnsupportedMediaExceptionException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new UnsupportedMediaTypeException("Unsupported Media"), HttpStatusCode.UnsupportedMediaType);
        }

        [Fact]
        public void GivenAnInvalidSearchOperationException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new InvalidSearchOperationException("Invalid search parameter."), HttpStatusCode.Forbidden);
        }

        [Fact]
        public void GivenASearchOperationNotSupportedException_WhenExecutingAnAction_ThenTheResponseShouldBeAnOperationOutcome()
        {
            ValidateOperationOutcome(new SearchOperationNotSupportedException("Not supported."), HttpStatusCode.Forbidden);
        }

        private OperationOutcome ValidateOperationOutcome(Exception exception, HttpStatusCode expectedStatusCode)
        {
            var filter = new OperationOutcomeExceptionFilterAttribute(_fhirContextAccessor);

            _context.Exception = exception;

            filter.OnActionExecuted(_context);

            var result = _context.Result as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(expectedStatusCode, result.StatusCode.GetValueOrDefault());
            Assert.Equal(_correlationId, result.Resource.Id);

            return Assert.IsType<OperationOutcome>(result.Resource);
        }
    }
}
