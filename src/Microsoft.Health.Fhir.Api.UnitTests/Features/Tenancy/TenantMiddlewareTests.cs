// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.Tenancy;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantMiddlewareTests
    {
        private static readonly TenantDescriptor Alpha = new(new TenantId("alpha"));

        private readonly ITenantResolver _resolver = Substitute.For<ITenantResolver>();
        private readonly ITenantRegistry _registry = Substitute.For<ITenantRegistry>();
        private readonly ITenantContainerCache _cache = Substitute.For<ITenantContainerCache>();
        private readonly ITenantContextAccessor _accessor = Substitute.For<ITenantContextAccessor>();

        public TenantMiddlewareTests()
        {
            _accessor.Current.Returns(TenantId.Default);
        }

        [Fact]
        public async Task GivenAKnownTenant_WhenARequestIsHandled_ThenRequestServicesComeFromTheTenantContainer()
        {
            var tenantServices = new ServiceCollection();
            tenantServices.AddScoped<TenantMarker>();
            ServiceProvider tenantProvider = tenantServices.BuildServiceProvider();

            ArrangeResolution(tenantProvider);

            IServiceProvider observed = null;
            TenantMarker marker = null;
            HttpContext context = CreateContext();
            IServiceProvider original = context.RequestServices;

            await CreateMiddleware(ctx =>
            {
                observed = ctx.RequestServices;
                marker = ctx.RequestServices.GetRequiredService<TenantMarker>();
                return Task.CompletedTask;
            }).InvokeAsync(context);

            Assert.NotNull(observed);
            Assert.NotSame(original, observed);
            Assert.NotNull(marker);
            Assert.Same(original, context.RequestServices);
        }

        [Fact]
        public async Task GivenAKnownTenant_WhenARequestIsHandled_ThenTheTenantContextIsSetAndRestored()
        {
            TenantId previousTenant = new("existing");
            _accessor.Current.Returns(previousTenant);
            ArrangeResolution(new ServiceCollection().BuildServiceProvider());

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(CreateContext());

            Received.InOrder(() =>
            {
                _accessor.SetCurrent(Alpha.TenantId);
                _accessor.SetCurrent(previousTenant);
            });
        }

        [Fact]
        public async Task GivenAKnownTenant_WhenTheDownstreamPipelineThrows_ThenTheLeaseIsStillReleasedAndStateIsRestored()
        {
            ITenantLease lease = ArrangeResolution(new ServiceCollection().BuildServiceProvider());
            HttpContext context = CreateContext();
            IServiceProvider original = context.RequestServices;

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateMiddleware(_ => throw new InvalidOperationException("boom"))
                    .InvokeAsync(context));

            Assert.Same(original, context.RequestServices);
            lease.Received(1).Dispose();
            _accessor.Received(1).SetCurrent(TenantId.Default);
        }

        [Fact]
        public async Task GivenAnUnresolvableHost_WhenARequestIsHandled_ThenNotFoundIsReturnedWithoutTouchingRegistryCacheOrAmbientTenant()
        {
            _resolver.TryResolve(Arg.Any<HttpContext>(), out Arg.Any<TenantId>()).Returns(false);

            HttpContext context = CreateContext();
            bool nextCalled = false;

            await CreateMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.False(nextCalled);
            Assert.Empty(_registry.ReceivedCalls());
            Assert.Empty(_cache.ReceivedCalls());
            Assert.Empty(_accessor.ReceivedCalls());
        }

        [Fact]
        public async Task GivenAnUnknownTenant_WhenARequestIsHandled_ThenNotFoundIsReturnedWithoutTouchingCacheOrAmbientTenant()
        {
            _resolver
                .TryResolve(Arg.Any<HttpContext>(), out Arg.Any<TenantId>())
                .Returns(x =>
                {
                    x[1] = Alpha.TenantId;
                    return true;
                });

            _registry
                .TryGetTenant(Alpha.TenantId, out Arg.Any<TenantDescriptor>())
                .Returns(false);

            HttpContext context = CreateContext();

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.Empty(_cache.ReceivedCalls());
            Assert.Empty(_accessor.ReceivedCalls());
        }

        [Fact]
        public async Task GivenAdmissionIsRejected_WhenARequestIsHandled_ThenServiceUnavailableWithRetryAfterIsReturned()
        {
            _resolver
                .TryResolve(Arg.Any<HttpContext>(), out Arg.Any<TenantId>())
                .Returns(x =>
                {
                    x[1] = Alpha.TenantId;
                    return true;
                });

            _registry
                .TryGetTenant(Alpha.TenantId, out Arg.Any<TenantDescriptor>())
                .Returns(x =>
                {
                    x[1] = Alpha;
                    return true;
                });

            _cache
                .AcquireAsync(Alpha, Arg.Any<CancellationToken>())
                .Returns<ValueTask<ITenantLease>>(_ => throw new TenantAdmissionRejectedException(Alpha.TenantId, 100));

            HttpContext context = CreateContext();

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
            Assert.Equal("5", context.Response.Headers.RetryAfter);
        }

        [Fact]
        public async Task GivenAdmissionIsCanceled_WhenARequestIsHandled_ThenOperationCanceledExceptionIsNotTranslatedToServiceUnavailable()
        {
            _resolver
                .TryResolve(Arg.Any<HttpContext>(), out Arg.Any<TenantId>())
                .Returns(x =>
                {
                    x[1] = Alpha.TenantId;
                    return true;
                });

            _registry
                .TryGetTenant(Alpha.TenantId, out Arg.Any<TenantDescriptor>())
                .Returns(x =>
                {
                    x[1] = Alpha;
                    return true;
                });

            _cache
                .AcquireAsync(Alpha, Arg.Any<CancellationToken>())
                .Returns<ValueTask<ITenantLease>>(_ => throw new OperationCanceledException());

            HttpContext context = CreateContext();

            await Assert.ThrowsAsync<OperationCanceledException>(() => CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context));

            Assert.NotEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
            Assert.Empty(_accessor.ReceivedCalls());
        }

        [Fact]
        public async Task GivenTenantScopeCreationFails_WhenARequestIsHandled_ThenTheLeaseIsReleasedAndStateIsRestored()
        {
            var scopeFactory = Substitute.For<IServiceScopeFactory>();
            scopeFactory.CreateScope().Returns(_ => throw new InvalidOperationException("scope creation failed"));

            IServiceProvider tenantProvider = Substitute.For<IServiceProvider>();
            tenantProvider.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);

            ITenantLease lease = ArrangeResolution(tenantProvider);
            HttpContext context = CreateContext();
            IServiceProvider original = context.RequestServices;

            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context));

            Assert.Same(original, context.RequestServices);
            Received.InOrder(() =>
            {
                _accessor.SetCurrent(Alpha.TenantId);
                _accessor.SetCurrent(TenantId.Default);
                lease.Dispose();
            });
        }

        [Fact]
        public async Task GivenATenantScope_WhenTheRequestCompletes_ThenTenantScopedServicesAreDisposedBeforeTheLeaseIsReleased()
        {
            bool leaseReleased = false;
            var events = new List<string>();

            var tenantServices = new ServiceCollection();
            tenantServices.AddScoped(_ => new OrderedTenantScopeService(() =>
            {
                Assert.False(leaseReleased);
                events.Add("scope");
                return ValueTask.CompletedTask;
            }));

            await using ServiceProvider tenantProvider = tenantServices.BuildServiceProvider();

            ITenantLease lease = ArrangeResolution(tenantProvider);
            lease.When(x => x.Dispose()).Do(_ =>
            {
                leaseReleased = true;
                events.Add("lease");
            });

            HttpContext context = CreateContext();

            await CreateMiddleware(ctx =>
            {
                _ = ctx.RequestServices.GetRequiredService<OrderedTenantScopeService>();
                return Task.CompletedTask;
            }).InvokeAsync(context);

            Assert.Collection(
                events,
                item => Assert.Equal("scope", item),
                item => Assert.Equal("lease", item));
        }

        private ITenantLease ArrangeResolution(IServiceProvider tenantProvider)
        {
            _resolver
                .TryResolve(Arg.Any<HttpContext>(), out Arg.Any<TenantId>())
                .Returns(x =>
                {
                    x[1] = Alpha.TenantId;
                    return true;
                });

            _registry
                .TryGetTenant(Alpha.TenantId, out Arg.Any<TenantDescriptor>())
                .Returns(x =>
                {
                    x[1] = Alpha;
                    return true;
                });

            ITenantLease lease = Substitute.For<ITenantLease>();
            lease.TenantId.Returns(Alpha.TenantId);
            lease.Services.Returns(tenantProvider);

            _cache.AcquireAsync(Alpha, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(lease));

            return lease;
        }

        private TenantMiddleware CreateMiddleware(RequestDelegate next) =>
            new(next, _resolver, _registry, _cache, _accessor, NullLogger<TenantMiddleware>.Instance);

        private static HttpContext CreateContext()
        {
            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("alpha.example.org");
            context.RequestServices = new ServiceCollection().BuildServiceProvider();
            context.Response.Body = new MemoryStream();
            return context;
        }

        private sealed class OrderedTenantScopeService(Func<ValueTask> disposeAsync)
            : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => disposeAsync();
        }

        private sealed class TenantMarker
        {
        }
    }
}
