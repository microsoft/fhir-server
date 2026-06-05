// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchParameters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterRetryPolicyFactoryTests
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly IFhirRequestContext _fhirRequestContext;

        public SearchParameterRetryPolicyFactoryTests()
        {
            _fhirRequestContext = Substitute.For<IFhirRequestContext>();
            _requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            _requestContextAccessor.RequestContext.Returns(_fhirRequestContext);
        }

        [Fact]
        public async Task GivenNoLastUpdatedInContext_WhenConcurrencyConflictOccurs_ThenRetries()
        {
            var properties = new Dictionary<string, object>();
            _fhirRequestContext.Properties.Returns(properties);
            var attemptCount = 0;
            var maxAttempts = 4; // 1 initial + 3 retries

            var exception = await Assert.ThrowsAsync<BadRequestException>(async () =>
            {
                await SearchParameterRetryPolicyFactory.ExecuteAsync(
                    _requestContextAccessor,
                    async () =>
                    {
                        attemptCount++;
                        await Task.CompletedTask;
                        throw new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict);
                    });
            });

            Assert.Equal(maxAttempts, attemptCount);
            Assert.Contains("Retries=3", exception.Message);
        }

        [Fact]
        public async Task GivenNoLastUpdatedInContext_WhenOperationSucceeds_ThenNoRetry()
        {
            var properties = new Dictionary<string, object>();
            _fhirRequestContext.Properties.Returns(properties);
            var attemptCount = 0;

            var result = await SearchParameterRetryPolicyFactory.ExecuteAsync(
                _requestContextAccessor,
                async () =>
                {
                    attemptCount++;
                    await Task.CompletedTask;
                    return "success";
                });

            Assert.Equal(1, attemptCount);
            Assert.Equal("success", result);
        }

        [Fact]
        public async Task GivenLastUpdatedInContext_WhenConcurrencyConflictOccurs_ThenNoRetry()
        {
            var lastUpdated = DateTimeOffset.UtcNow;
            var properties = new Dictionary<string, object>
            {
                [SearchParameterRequestContextPropertyNames.LastUpdated] = lastUpdated,
            };
            _fhirRequestContext.Properties.Returns(properties);
            var attemptCount = 0;

            var exception = await Assert.ThrowsAsync<BadRequestException>(async () =>
            {
                await SearchParameterRetryPolicyFactory.ExecuteAsync(
                    _requestContextAccessor,
                    async () =>
                    {
                        attemptCount++;
                        await Task.CompletedTask;
                        throw new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict);
                    });
            });

            Assert.Equal(1, attemptCount);
            Assert.DoesNotContain("Retries=", exception.Message);
        }

        [Fact]
        public async Task GivenNoLastUpdatedInContext_WhenSucceedsAfterRetry_ThenReturnsResult()
        {
            var properties = new Dictionary<string, object>();
            _fhirRequestContext.Properties.Returns(properties);
            var attemptCount = 0;

            var result = await SearchParameterRetryPolicyFactory.ExecuteAsync(
                _requestContextAccessor,
                async () =>
                {
                    attemptCount++;
                    await Task.CompletedTask;

                    if (attemptCount < 3)
                    {
                        throw new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict);
                    }

                    return "success";
                });

            Assert.Equal(3, attemptCount);
            Assert.Equal("success", result);
        }

        [Fact]
        public async Task GivenNonGenericOverload_WhenOperationSucceeds_ThenCompletes()
        {
            var properties = new Dictionary<string, object>();
            _fhirRequestContext.Properties.Returns(properties);
            var executed = false;

            await SearchParameterRetryPolicyFactory.ExecuteAsync(
                _requestContextAccessor,
                async () =>
                {
                    await Task.CompletedTask;
                    executed = true;
                });

            Assert.True(executed);
        }

        [Fact]
        public async Task GivenOnRetryCallback_WhenRetries_ThenCallbackInvoked()
        {
            var properties = new Dictionary<string, object>();
            _fhirRequestContext.Properties.Returns(properties);
            var callbackCount = 0;
            var attemptCount = 0;

            await Assert.ThrowsAsync<BadRequestException>(async () =>
            {
                await SearchParameterRetryPolicyFactory.ExecuteAsync(
                    _requestContextAccessor,
                    async () =>
                    {
                        attemptCount++;
                        await Task.CompletedTask;
                        throw new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict);
                    },
                    (ex, ts, rc) =>
                    {
                        callbackCount++;
                        Assert.IsType<BadRequestException>(ex);
                        Assert.True(rc > 0 && rc <= 3);
                    });
            });

            Assert.Equal(3, callbackCount);
            Assert.Equal(4, attemptCount); // 1 initial + 3 retries
        }

        [Fact]
        public async Task GivenNonConcurrencyException_WhenThrown_ThenNoRetry()
        {
            var properties = new Dictionary<string, object>();
            _fhirRequestContext.Properties.Returns(properties);
            var attemptCount = 0;

            var exception = await Assert.ThrowsAsync<BadRequestException>(async () =>
            {
                await SearchParameterRetryPolicyFactory.ExecuteAsync(
                    _requestContextAccessor,
                    async () =>
                    {
                        attemptCount++;
                        await Task.CompletedTask;
                        throw new BadRequestException("some other error");
                    });
            });

            Assert.Equal(1, attemptCount);
            Assert.DoesNotContain("Retries=", exception.Message);
        }

        [Fact]
        public async Task GivenLastUpdatedInContext_WhenOperationSucceeds_ThenExecutesDirectly()
        {
            var lastUpdated = DateTimeOffset.UtcNow;
            var properties = new Dictionary<string, object>
            {
                [SearchParameterRequestContextPropertyNames.LastUpdated] = lastUpdated,
            };
            _fhirRequestContext.Properties.Returns(properties);
            var executed = false;

            var result = await SearchParameterRetryPolicyFactory.ExecuteAsync(
                _requestContextAccessor,
                async () =>
                {
                    await Task.CompletedTask;
                    executed = true;
                    return "direct-success";
                });

            Assert.True(executed);
            Assert.Equal("direct-success", result);
        }
    }
}
