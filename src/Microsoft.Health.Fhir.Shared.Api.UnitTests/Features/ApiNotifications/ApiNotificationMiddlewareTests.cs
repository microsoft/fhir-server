// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.ApiNotifications;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Notifications
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class ApiNotificationMiddlewareTests
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly IFhirRequestContext _fhirRequestContext = new DefaultFhirRequestContext();
        private readonly IMediator _mediator = Substitute.For<IMediator>();

        private readonly RequestDelegate _next = httpContext => Task.CompletedTask;
        private readonly ApiNotificationMiddleware _apiNotificationMiddleware;
        private readonly HttpContext _httpContext = new DefaultHttpContext();

        public ApiNotificationMiddlewareTests()
        {
            _fhirRequestContextAccessor.RequestContext.Returns(_fhirRequestContext);

            _apiNotificationMiddleware = new ApiNotificationMiddleware(
                    _fhirRequestContextAccessor,
                    _mediator,
                    NullLogger<ApiNotificationMiddleware>.Instance);
        }

        [Fact]
        public async Task GivenRequestPath_WhenInvoked_DoesNotLogForHealthCheck()
        {
            _httpContext.Request.Path = new PathString(KnownRoutes.HealthCheck);

            await _apiNotificationMiddleware.InvokeAsync(_httpContext, _next);

            await _mediator.DidNotReceiveWithAnyArgs().Publish(Arg.Any<object>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAuditEventTypeNotNull_WhenInvoked_EmitsMediatRApiAndStorageEvents()
        {
            _httpContext.Request.Path = "/Observation";
            _fhirRequestContext.AuditEventType = "read";

            await _apiNotificationMiddleware.InvokeAsync(_httpContext, _next);

            await _mediator.ReceivedWithAnyArgs(1).Publish(Arg.Any<ApiResponseNotification>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenRequestPath_AndNullFhirRequestContext_WhenInvoked_DoesNotFail_AndDoesNotEmitMediatREvents()
        {
            _fhirRequestContextAccessor.RequestContext.Returns((IFhirRequestContext)null);

            _httpContext.Request.Path = "/Observation";
            await _apiNotificationMiddleware.InvokeAsync(_httpContext, _next);

            await _mediator.DidNotReceiveWithAnyArgs().Publish(Arg.Any<object>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenRequestPath_WhenMediatRFails_NoExceptionIsThrown()
        {
            await Task.CompletedTask;
            _mediator.WhenForAnyArgs(async x => await x.Publish(Arg.Any<ApiResponseNotification>(), Arg.Any<CancellationToken>())).Throw(new System.Exception("Failure"));

            _httpContext.Request.Path = "/Observation";
            await _apiNotificationMiddleware.InvokeAsync(_httpContext, _next);

            await _mediator.DidNotReceiveWithAnyArgs().Publish(Arg.Any<object>(), Arg.Any<CancellationToken>());
        }
    }
}
