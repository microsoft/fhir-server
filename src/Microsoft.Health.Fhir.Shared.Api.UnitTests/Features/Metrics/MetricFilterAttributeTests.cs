// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.ApiNotifications;
using Microsoft.Health.Fhir.Api.Features.Metrics;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Metrics;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Metrics
{
    public class MetricFilterAttributeTests
    {
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IFhirRequestContext _fhirRequestContext = Substitute.For<IFhirRequestContext>();
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly ILogger<ApiResponseMetricFilterAttribute> _logger = NullLogger<ApiResponseMetricFilterAttribute>.Instance;

        private const string ControllerName = "controller";
        private const string ActionName = "action";

        private readonly ApiResponseMetricFilterAttribute _filter;
        private readonly HttpContext _httpContext = new DefaultHttpContext();
        private ActionExecutingContext _actionExecutingContext;
        private ActionExecutedContext _actionExecutedContext;

        public MetricFilterAttributeTests()
        {
            _fhirRequestContextAccessor.FhirRequestContext.Returns(_fhirRequestContext);
            _filter = new ApiResponseMetricFilterAttribute(_mediator, _fhirRequestContextAccessor, _logger);

            _actionExecutingContext = new ActionExecutingContext(
                new ActionContext(_httpContext, new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executing Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                new MockController());

            _actionExecutedContext = new ActionExecutedContext(
                new ActionContext(_httpContext, new RouteData(), new ControllerActionDescriptor() { DisplayName = "Executing Context Test Descriptor" }),
                new List<IFilterMetadata>(),
                new MockController());

            _actionExecutingContext.ActionDescriptor = new ControllerActionDescriptor()
            {
                ControllerName = ControllerName,
                ActionName = ActionName,
            };
        }

        [Fact]
        public async Task GivenRequestPathButNoStorageRequestMetrics_WhenInvoked_EmitsMediatRApiEvents()
        {
            _httpContext.Request.Path = "/Observations";
            ////_fhirRequestContext.StorageRequestMetrics = null;
            await _filter.OnActionExecutionAsync(_actionExecutingContext, () => Task.FromResult(_actionExecutedContext));

            await _mediator.Received(1).Publish<ApiResponseMetricNotification>(Arg.Any<ApiResponseMetricNotification>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenRequestPathAndStorageRequestMetrics_WhenInvoked_EmitsMediatRApiAndStorageEvents()
        {
            _httpContext.Request.Path = "/Observation";
            ////_fhirRequestContext.StorageRequestMetrics = Substitute.For<IMetricsNotification>();
            await _filter.OnActionExecutionAsync(_actionExecutingContext, () => Task.FromResult(_actionExecutedContext));

            await _mediator.Received(1).Publish<ApiResponseMetricNotification>(Arg.Any<ApiResponseMetricNotification>(), Arg.Any<CancellationToken>());
            ////await _mediator.Received(1).Publish<IMetricsNotification>(Arg.Is<IMetricsNotification>(_fhirRequestContext.StorageRequestMetrics), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenRequestPath_AndNullFhirRequestContext_WhenInvoked_DoesNotFail_AndDoesNotEmitMediatREvents()
        {
            _fhirRequestContextAccessor.FhirRequestContext.Returns((IFhirRequestContext)null);

            _httpContext.Request.Path = "/Observation";
            ////_fhirRequestContext.StorageRequestMetrics = Substitute.For<IMetricsNotification>();
            await _filter.OnActionExecutionAsync(_actionExecutingContext, () => Task.FromResult(_actionExecutedContext));

            await _mediator.DidNotReceiveWithAnyArgs().Publish(Arg.Any<object>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenRequestPath_WhenMediatRFails_NoExceptionIsThrown()
        {
            await Task.CompletedTask;
            _mediator.WhenForAnyArgs(async x => await x.Publish(Arg.Any<ApiResponseMetricNotification>(), Arg.Any<CancellationToken>())).Throw(new System.Exception("Failure"));

            _httpContext.Request.Path = "/Observation";
            ////_fhirRequestContext.StorageRequestMetrics = Substitute.For<IMetricsNotification>();
            await _filter.OnActionExecutionAsync(_actionExecutingContext, () => Task.FromResult(_actionExecutedContext));

            await _mediator.DidNotReceiveWithAnyArgs().Publish(Arg.Any<object>(), Arg.Any<CancellationToken>());
        }

        private class MockController : Controller
        {
        }
    }
}
