// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Internal;
using Microsoft.Health.Fhir.Api.Modules.HealthChecks;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.HealthCheck
{
    public class HealthCheckCachingTests
    {
        private readonly CachedHealthCheck _cahcedHealthCheck;
        private readonly HealthCheckContext _context;
        private Func<IServiceProvider, IHealthCheck> _healthCheckFunc;
        private IHealthCheck _healthCheck;
        private IServiceScope _serviceScope;

        public HealthCheckCachingTests()
        {
            var serviceProvider = Substitute.For<IServiceProvider>();
            var scopeFactory = Substitute.For<IServiceScopeFactory>();
            serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);

            _serviceScope = Substitute.For<IServiceScope>();
            scopeFactory.CreateScope().Returns(_serviceScope);

            _healthCheckFunc = Substitute.For<Func<IServiceProvider, IHealthCheck>>();
            _healthCheck = Substitute.For<IHealthCheck>();
            _context = new HealthCheckContext();

            _cahcedHealthCheck = new CachedHealthCheck(serviceProvider, _healthCheckFunc, NullLogger<CachedHealthCheck>.Instance);

            _healthCheckFunc.Invoke(Arg.Any<IServiceProvider>()).ReturnsForAnyArgs(_healthCheck);

            _healthCheck.CheckHealthAsync(Arg.Any<HealthCheckContext>()).Returns(HealthCheckResult.Healthy());
        }

        [Fact]
        public async Task GivenTheHealthCheckCache_WhenCallingWithMultipleRequests_ThenOnlyOneResultShouldBeExecuted()
        {
            await Task.WhenAll(
                _cahcedHealthCheck.CheckHealthAsync(_context, CancellationToken.None),
                _cahcedHealthCheck.CheckHealthAsync(_context, CancellationToken.None),
                _cahcedHealthCheck.CheckHealthAsync(_context, CancellationToken.None),
                _cahcedHealthCheck.CheckHealthAsync(_context, CancellationToken.None));

            _healthCheckFunc.Received(1).Invoke(_serviceScope.ServiceProvider);
        }

        [Fact]
        public async Task GivenTheHealthCheckCache_WhenRequestingStatus_ThenTheResultIsWrittenCorrectly()
        {
            var result = await _cahcedHealthCheck.CheckHealthAsync(_context, CancellationToken.None);

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }

        [Fact]
        public async Task GivenTheHealthCheckCache_WhenHealthCheckThrows_ThenTheResultIsWrittenCorrectly()
        {
            _healthCheck.When(x => x.CheckHealthAsync(Arg.Any<HealthCheckContext>())).Throw<Exception>();

            var result = await _cahcedHealthCheck.CheckHealthAsync(_context, CancellationToken.None);

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
        }

        [Fact]
        public async Task GivenTheHealthCheckCache_WhenMoreThan1SecondApart_ThenSecondRequestGetsFreshResults()
        {
            // Mocks the time a second ago so we can call the middleware in the past
            using (Mock.Property(() => ClockResolver.UtcNowFunc, () => DateTimeOffset.UtcNow.AddSeconds(-1)))
            {
                await Task.WhenAll(
                    _cahcedHealthCheck.CheckHealthAsync(_context, CancellationToken.None),
                    _cahcedHealthCheck.CheckHealthAsync(_context, CancellationToken.None));
            }

            // Call the middleware again to ensure we get new results
            await _cahcedHealthCheck.CheckHealthAsync(_context, CancellationToken.None);

            _healthCheckFunc.Received(2).Invoke(_serviceScope.ServiceProvider);
        }

        [Fact]
        public async Task GivenTheHealthCheckCache_WhenCancellationIsRequested_ThenWeDoNotThrowAndReturnLastHealthCheckResult()
        {
            // Trigger a health check so as to populate lastResult
            await _cahcedHealthCheck.CheckHealthAsync(_context, CancellationToken.None);

            var ctSource = new CancellationTokenSource();
            ctSource.Cancel();

            HealthCheckResult result = await _cahcedHealthCheck.CheckHealthAsync(_context, ctSource.Token);

            // Confirm we only called CheckHealthAsync once.
            await _healthCheck.Received(1).CheckHealthAsync(Arg.Any<HealthCheckContext>(), Arg.Any<CancellationToken>());
            Assert.Equal(HealthStatus.Healthy, result.Status);
        }
    }
}
