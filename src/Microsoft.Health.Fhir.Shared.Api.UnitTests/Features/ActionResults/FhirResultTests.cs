// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Hl7.Fhir.ElementModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.ActionResults
{
    public class FhirResultTests
    {
        [Fact]
        public void GivenAGoneStatus_WhenReturningAResult_ThenTheContentShouldBeEmpty()
        {
            var result = FhirResult.Gone();
            var context = new ActionContext
            {
                HttpContext = new DefaultHttpContext(),
            };

            result.ExecuteResult(context);

            Assert.Null(result.Result);
            Assert.Equal(HttpStatusCode.Gone, result.StatusCode.GetValueOrDefault());
            Assert.Equal(0, context.HttpContext.Request.Body.Length);
        }

        [Fact]
        public void GivenANoContentStatus_WhenReturningAResult_ThenTheStatusCodeIsSetCorrectly()
        {
            var result = FhirResult.NoContent();
            var context = new ActionContext
            {
                HttpContext = new DefaultHttpContext(),
            };

            result.ExecuteResult(context);

            Assert.Null(result.Result);
            Assert.Equal(HttpStatusCode.NoContent, result.StatusCode.GetValueOrDefault());
        }

        [Fact]
        public void GivenANotFoundStatus_WhenReturningAResult_ThenTheStatusCodeIsSetCorrectly()
        {
            var result = FhirResult.NotFound();
            var context = new ActionContext
            {
                HttpContext = new DefaultHttpContext(),
            };

            result.ExecuteResult(context);

            Assert.Null(result.Result);
            Assert.Equal(HttpStatusCode.NotFound, result.StatusCode.GetValueOrDefault());
        }

        [Fact]
        public void GivenASuccessfulBundleProcessingStatus_WhenReturningAResult_ThenBadRequestIsReturned()
        {
            var bundleResponse = new BundleResponse(Substitute.For<ResourceElement>(Substitute.For<ITypedElement>()), Core.Features.Persistence.BundleProcessingStatus.SUCCEEDED);

            var result = FhirResult.Create(bundleResponse);

            var context = new ActionContext
            {
                HttpContext = new DefaultHttpContext(),
            };

            result.ExecuteResult(context);

            Assert.Equal(HttpStatusCode.OK, result.StatusCode.GetValueOrDefault());
        }

        [Fact]
        public void GivenAFailedBundleProcessingStatus_WhenReturningAResult_ThenBadRequestIsReturned()
        {
            var bundleResponse = new BundleResponse(Substitute.For<ResourceElement>(Substitute.For<ITypedElement>()), Core.Features.Persistence.BundleProcessingStatus.FAILED);

            var result = FhirResult.Create(bundleResponse);

            var context = new ActionContext
            {
                HttpContext = new DefaultHttpContext(),
            };

            result.ExecuteResult(context);

            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode.GetValueOrDefault());
        }
    }
}
