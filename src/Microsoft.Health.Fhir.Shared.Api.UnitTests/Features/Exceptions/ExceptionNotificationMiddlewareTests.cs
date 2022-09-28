// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.ExceptionNotifications;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.Exceptions
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class ExceptionNotificationMiddlewareTests
    {
        private readonly DefaultHttpContext _context;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IMediator _mediator = Substitute.For<IMediator>();

        public ExceptionNotificationMiddlewareTests()
        {
            _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            _context = new DefaultHttpContext();
        }

        [Fact]
        public async Task GivenAnHttpContextWithException_WhenExecutingExceptionNotificationMiddleware_MediatRShouldEmitNotification()
        {
            var exceptionMessage = "Test exception";
            var exceptionNotificationMiddleware = CreateExceptionNotificationMiddleware(innerHttpContext => throw new Exception(exceptionMessage));

            try
            {
                await exceptionNotificationMiddleware.Invoke(_context);
            }
            catch (Exception e)
            {
                await _mediator.ReceivedWithAnyArgs(1).Publish(Arg.Any<ExceptionNotification>(), Arg.Any<CancellationToken>());
                Assert.Equal(exceptionMessage, e.Message);
            }
        }

        [Fact]
        public async Task GivenAnHttpContextWithNoException_WhenExecutingExceptionNotificationMiddleware_MediatRShouldNotEmitNotification()
        {
            var exceptionNotificationMiddleware = CreateExceptionNotificationMiddleware(innerHttpContext => Task.CompletedTask);

            await exceptionNotificationMiddleware.Invoke(_context);

            await _mediator.DidNotReceiveWithAnyArgs().Publish(Arg.Any<object>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task WhenMediatRFails_OriginalExceptionStillThrown()
        {
            var exceptionMessage = "Test exception";
            var mediatorExceptionMessage = "Mediator Failure";

            var exceptionNotificationMiddleware = CreateExceptionNotificationMiddleware(innerHttpContext => throw new Exception(exceptionMessage));
            _mediator.WhenForAnyArgs(async x => await x.Publish(Arg.Any<ExceptionNotificationMiddleware>(), Arg.Any<CancellationToken>())).Throw(new System.Exception(mediatorExceptionMessage));

            try
            {
                await exceptionNotificationMiddleware.Invoke(_context);
            }
            catch (Exception e)
            {
                await _mediator.DidNotReceiveWithAnyArgs().Publish(Arg.Any<object>(), Arg.Any<CancellationToken>());
                Assert.Equal(exceptionMessage, e.Message);
            }
        }

        private ExceptionNotificationMiddleware CreateExceptionNotificationMiddleware(RequestDelegate nextDelegate)
        {
            return new ExceptionNotificationMiddleware(nextDelegate, NullLogger<ExceptionNotificationMiddleware>.Instance, _fhirRequestContextAccessor, _mediator);
        }
    }
}
