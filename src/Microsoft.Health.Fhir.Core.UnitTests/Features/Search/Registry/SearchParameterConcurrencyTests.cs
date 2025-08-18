// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Registry
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterConcurrencyTests
    {
        private const string TestUri1 = "http://test.com/searchparam1";
        private const string TestUri2 = "http://test.com/searchparam2";

        [Fact]
        public async Task GivenValidSearchParameterUri_WhenExecutingWithLock_ThenOperationCompletes()
        {
            // Arrange
            const string expectedResult = "test result";

            // Act
            var result = await SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, () =>
            {
                return Task.FromResult(expectedResult);
            });

            // Assert
            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public async Task GivenValidSearchParameterUri_WhenExecutingVoidOperation_ThenOperationCompletes()
        {
            // Arrange
            var executed = false;

            // Act
            await SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, () =>
            {
                executed = true;
                return Task.CompletedTask;
            });

            // Assert
            Assert.True(executed);
        }

        [Fact]
        public async Task GivenDifferentUris_WhenExecutingConcurrently_ThenBothComplete()
        {
            // Arrange
            var task1Completed = false;
            var task2Completed = false;

            // Act
            var tasks = new[]
            {
                SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, async () =>
                {
                    await Task.Delay(10);
                    task1Completed = true;
                }),
                SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri2, async () =>
                {
                    await Task.Delay(10);
                    task2Completed = true;
                }),
            };

            await Task.WhenAll(tasks);

            // Assert
            Assert.True(task1Completed);
            Assert.True(task2Completed);
        }

        [Fact]
        public async Task GivenExceptionInOperation_WhenExecuting_ThenExceptionIsPropagated()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Test exception");

            // Act & Assert
            var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, () =>
                {
                    throw expectedException;
                }));

            Assert.Same(expectedException, actualException);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GivenInvalidUri_WhenExecuting_ThenThrowsArgumentException(string invalidUri)
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                SearchParameterConcurrencyManager.ExecuteWithLockAsync(invalidUri, () => Task.FromResult(1)));
        }

        [Fact]
        public async Task GivenNullOperation_WhenExecuting_ThenThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                SearchParameterConcurrencyManager.ExecuteWithLockAsync<int>(TestUri1, null));

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, null));
        }

        [Fact]
        public void GivenSearchParameterConcurrencyException_WhenCreatedWithUris_ThenPropertiesAreSet()
        {
            // Arrange
            var uris = new[] { "http://test.com/param1", "http://test.com/param2" };

            // Act
            var exception = new SearchParameterConcurrencyException(uris);

            // Assert
            Assert.NotNull(exception.Message);
            Assert.Contains("param1", exception.Message);
            Assert.Contains("param2", exception.Message);
            Assert.Equal(2, exception.ConflictedUris.Count);
            Assert.Contains("http://test.com/param1", exception.ConflictedUris);
            Assert.Contains("http://test.com/param2", exception.ConflictedUris);
        }

        [Fact]
        public void GivenSearchParameterConcurrencyException_WhenCreatedWithMessage_ThenMessageIsSet()
        {
            // Arrange
            const string expectedMessage = "Test concurrency error";

            // Act
            var exception = new SearchParameterConcurrencyException(expectedMessage);

            // Assert
            Assert.Equal(expectedMessage, exception.Message);
            Assert.Empty(exception.ConflictedUris);
        }
    }
}
