// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.ApiNotifications;
using Microsoft.Health.Fhir.Core.Features.Context;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.Notifications
{
    public class ApiNotificationMiddlewareTests
    {
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IFhirRequestContext _fhirRequestContext = Substitute.For<IFhirRequestContext>();
        private readonly IMediator _mediator = Substitute.For<IMediator>();

        private readonly ApiNotificationMiddleware _apiNotificationMiddleware;
        private readonly HttpContext _httpContext = new DefaultHttpContext();

        public ApiNotificationMiddlewareTests()
        {
            _fhirRequestContextAccessor.FhirRequestContext.Returns(_fhirRequestContext);

            _apiNotificationMiddleware = new ApiNotificationMiddleware(
                    httpContext => Task.CompletedTask,
                    _fhirRequestContextAccessor,
                    _mediator,
                    NullLogger<ApiNotificationMiddleware>.Instance);
        }

        [Fact]
        public async Task GivenRequestPath_WhenInvoked_DoesNotLogForHealthCheck()
        {
            _httpContext.Request.Path = FhirServerApplicationBuilderExtensions.HealthCheckPath;

            await _apiNotificationMiddleware.Invoke(_httpContext);

            await _mediator.DidNotReceiveWithAnyArgs().Publish(Arg.Any<object>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenRequestPathButNoStorageContext_WhenInvoked_EmitsMediatRApiEvents()
        {
            _httpContext.Request.Path = "/Observations";
            _fhirRequestContext.StorageRequestMetrics = null;
            await _apiNotificationMiddleware.Invoke(_httpContext);

            await _mediator.ReceivedWithAnyArgs(1).Publish<ApiResponseNotification>(Arg.Any<ApiResponseNotification>(), Arg.Any<CancellationToken>());
            await _mediator.DidNotReceiveWithAnyArgs().Publish<CosmosStorageRequestMetrics>(Arg.Any<CosmosStorageRequestMetrics>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenRequestPathAndStorageContext_WhenInvoked_EmitsMediatRApiAndStorageEvents()
        {
            _httpContext.Request.Path = "/Observation";
            _fhirRequestContext.StorageRequestMetrics = new CosmosStorageRequestMetrics("search", "Observation");
            await _apiNotificationMiddleware.Invoke(_httpContext);

            await _mediator.ReceivedWithAnyArgs(1).Publish<ApiResponseNotification>(Arg.Any<ApiResponseNotification>(), Arg.Any<CancellationToken>());
            await _mediator.Received(1).Publish<IStorageRequestMetrics>(Arg.Is<IStorageRequestMetrics>(_fhirRequestContext.StorageRequestMetrics), Arg.Any<CancellationToken>());
        }
    }
}
