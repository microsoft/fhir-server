// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchParameters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterRetryTests
    {
        [Fact]
        public async Task GivenConcurrencyConflict_WhenRetriesExhausted_ThenThrowsWithRetryCount()
        {
            var attemptCount = 0;
            var maxAttempts = 4; // 1 initial + 3 retries

            var exception = await Assert.ThrowsAsync<BadRequestException>(async () =>
            {
                await SearchParameterRetry.ExecuteAsync(
                    async () =>
                    {
                        attemptCount++;
                        await Task.CompletedTask;
                        throw new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict);
                    });
            });

            Assert.Equal(maxAttempts, attemptCount);
            Assert.Contains(" .3", exception.Message);
        }

        [Fact]
        public async Task GivenSuccessfulOperation_WhenExecuted_ThenNoRetry()
        {
            var attemptCount = 0;

            var result = await SearchParameterRetry.ExecuteAsync(
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
        public async Task GivenNonGenericOverload_WhenOperationSucceeds_ThenCompletes()
        {
            var executed = false;

            await SearchParameterRetry.ExecuteAsync(
                async () =>
                {
                    await Task.CompletedTask;
                    executed = true;
                });

            Assert.True(executed);
        }

        [Fact]
        public async Task GivenNonConcurrencyException_WhenThrown_ThenNoRetry()
        {
            var attemptCount = 0;

            var exception = await Assert.ThrowsAsync<BadRequestException>(async () =>
            {
                await SearchParameterRetry.ExecuteAsync(
                    async () =>
                    {
                        attemptCount++;
                        await Task.CompletedTask;
                        throw new BadRequestException("some other error");
                    });
            });

            Assert.Equal(1, attemptCount);
            Assert.DoesNotContain(" .", exception.Message);
        }
    }
}
