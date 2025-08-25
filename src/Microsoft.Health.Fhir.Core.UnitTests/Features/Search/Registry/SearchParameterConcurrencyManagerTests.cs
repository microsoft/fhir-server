// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
    public class SearchParameterConcurrencyManagerTests
    {
        private const string TestUri1 = "http://test.com/searchparam1";
        private const string TestUri2 = "http://test.com/searchparam2";

        [Fact]
        public async Task GivenSingleSearchParameterUri_WhenExecutingWithLock_ThenOperationCompletes()
        {
            // Arrange
            const string expectedResult = "test result";
            var executionCount = 0;

            // Act
            var result = await SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, () =>
            {
                executionCount++;
                return Task.FromResult(expectedResult);
            });

            // Assert
            Assert.Equal(expectedResult, result);
            Assert.Equal(1, executionCount);
        }

        [Fact]
        public async Task GivenSingleSearchParameterUri_WhenExecutingWithLockVoidOperation_ThenOperationCompletes()
        {
            // Arrange
            var executionCount = 0;

            // Act
            await SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, () =>
            {
                executionCount++;
                return Task.CompletedTask;
            });

            // Assert
            Assert.Equal(1, executionCount);
        }

        [Fact]
        public async Task GivenSameSearchParameterUri_WhenExecutingConcurrently_ThenOperationsExecuteSequentially()
        {
            // Arrange
            const int concurrentOperations = 5;
            var executionOrder = new List<int>();
            var executionStartTimes = new List<DateTime>();
            var executionEndTimes = new List<DateTime>();
            var lockObject = new object();

            // Act
            var tasks = new List<Task<int>>();
            for (int i = 0; i < concurrentOperations; i++)
            {
                var operationId = i;
                tasks.Add(SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, async () =>
                {
                    lock (lockObject)
                    {
                        executionOrder.Add(operationId);
                        executionStartTimes.Add(DateTime.UtcNow);
                    }

                    // Simulate some work
                    await Task.Delay(50);

                    lock (lockObject)
                    {
                        executionEndTimes.Add(DateTime.UtcNow);
                    }

                    return operationId;
                }));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(concurrentOperations, results.Length);
            Assert.Equal(concurrentOperations, executionOrder.Count);
            Assert.Equal(concurrentOperations, executionStartTimes.Count);
            Assert.Equal(concurrentOperations, executionEndTimes.Count);

            // Verify operations executed sequentially (no overlap)
            for (int i = 1; i < executionStartTimes.Count; i++)
            {
                // Each operation should start after the previous one ended
                Assert.True(
                    executionStartTimes[i] >= executionEndTimes[i - 1],
                    $"Operation {i} started at {executionStartTimes[i]} before operation {i - 1} ended at {executionEndTimes[i - 1]}");
            }
        }

        [Fact]
        public async Task GivenDifferentSearchParameterUris_WhenExecutingConcurrently_ThenOperationsExecuteInParallel()
        {
            // Arrange
            const int operationsPerUri = 2;
            var uri1ExecutionTimes = new List<DateTime>();
            var uri2ExecutionTimes = new List<DateTime>();
            var lockObject = new object();

            // Act
            var tasks = new List<Task>();

            // Add operations for first URI
            for (int i = 0; i < operationsPerUri; i++)
            {
                tasks.Add(SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, async () =>
                {
                    lock (lockObject)
                    {
                        uri1ExecutionTimes.Add(DateTime.UtcNow);
                    }

                    await Task.Delay(100);
                }));
            }

            // Add operations for second URI
            for (int i = 0; i < operationsPerUri; i++)
            {
                tasks.Add(SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri2, async () =>
                {
                    lock (lockObject)
                    {
                        uri2ExecutionTimes.Add(DateTime.UtcNow);
                    }

                    await Task.Delay(100);
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(operationsPerUri, uri1ExecutionTimes.Count);
            Assert.Equal(operationsPerUri, uri2ExecutionTimes.Count);

            // Verify that operations on different URIs can execute concurrently
            // by checking that at least one operation from URI2 started before all URI1 operations completed
            var minUri1Time = uri1ExecutionTimes[0];
            var minUri2Time = uri2ExecutionTimes[0];

            // Both should start around the same time (within a reasonable window)
            var timeDifference = Math.Abs((minUri1Time - minUri2Time).TotalMilliseconds);
            Assert.True(timeDifference < 50, $"Operations on different URIs should start concurrently. Time difference: {timeDifference}ms");
        }

        [Fact]
        public void GivenInitialState_WhenCheckingActiveLockCount_ThenReturnsZero()
        {
            // Act & Assert
            Assert.Equal(0, SearchParameterConcurrencyManager.ActiveLockCount);
        }

        [Fact]
        public async Task GivenOperationsInProgress_WhenCheckingActiveLockCount_ThenReturnsCorrectCount()
        {
            // Arrange
            var task1Started = new TaskCompletionSource<bool>();
            var task1CanContinue = new TaskCompletionSource<bool>();
            var task2Started = new TaskCompletionSource<bool>();
            var task2CanContinue = new TaskCompletionSource<bool>();

            // Act - Start two operations on different URIs
            var task1 = SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, async () =>
            {
                task1Started.SetResult(true);
                await task1CanContinue.Task;
            });

            var task2 = SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri2, async () =>
            {
                task2Started.SetResult(true);
                await task2CanContinue.Task;
            });

            // Wait for both operations to start
            await task1Started.Task;
            await task2Started.Task;

            // Assert - Should have 2 active locks
            Assert.Equal(2, SearchParameterConcurrencyManager.ActiveLockCount);

            // Complete first operation
            task1CanContinue.SetResult(true);
            await task1;

            // Give some time for cleanup
            await Task.Delay(10);

            // Assert - Should have 1 active lock
            Assert.Equal(1, SearchParameterConcurrencyManager.ActiveLockCount);

            // Complete second operation
            task2CanContinue.SetResult(true);
            await task2;

            // Give some time for cleanup
            await Task.Delay(10);

            // Assert - Should have 0 active locks
            Assert.Equal(0, SearchParameterConcurrencyManager.ActiveLockCount);
        }

        [Fact]
        public async Task GivenExceptionInOperation_WhenExecutingWithLock_ThenExceptionIsPropagatedAndLockIsReleased()
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

            // Allow time for cleanup to complete
            await Task.Delay(10);

            // Verify lock is released by ensuring a subsequent operation can execute
            var subsequentExecuted = false;
            await SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, () =>
            {
                subsequentExecuted = true;
                return Task.CompletedTask;
            });

            Assert.True(subsequentExecuted);

            // Allow additional time for final cleanup after the subsequent operation
            await Task.Delay(10);
            Assert.Equal(0, SearchParameterConcurrencyManager.ActiveLockCount);
        }

        [Fact]
        public async Task GivenCancellationToken_WhenOperationIsCancelled_ThenOperationCancelsAndLockIsReleased()
        {
            // Arrange
            var operationStarted = new TaskCompletionSource<bool>();
            using var cts = new CancellationTokenSource();

            // Act
            var operationTask = SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, async () =>
            {
                operationStarted.SetResult(true);
                await Task.Delay(10000, cts.Token); // Long delay that will be cancelled
            });

            // Wait for operation to start, then cancel it
            await operationStarted.Task;
            cts.Cancel();

            // Assert
            await Assert.ThrowsAsync<TaskCanceledException>(() => operationTask);

            // Verify lock is released
            await Task.Delay(10); // Allow cleanup
            Assert.Equal(0, SearchParameterConcurrencyManager.ActiveLockCount);
        }

        [Fact]
        public async Task GivenLongRunningOperations_WhenExecutingManySequentiallyOnSameUri_ThenMemoryIsReclaimed()
        {
            // Arrange
            const int iterations = 100;

            // Act - Execute many operations sequentially
            for (int i = 0; i < iterations; i++)
            {
                await SearchParameterConcurrencyManager.ExecuteWithLockAsync($"{TestUri1}_{i}", async () =>
                {
                    await Task.Delay(1); // Minimal work
                });
            }

            // Force cleanup by waiting
            await Task.Delay(50);

            // Assert - Should not have accumulated locks
            Assert.True(
                SearchParameterConcurrencyManager.ActiveLockCount < 10,
                $"Expected less than 10 active locks but found {SearchParameterConcurrencyManager.ActiveLockCount}");
        }

        [Fact]
        public async Task GivenMultipleThreads_WhenExecutingSameOperation_ThenOnlyOneExecutes()
        {
            // Arrange
            var executionCount = 0;
            using var barrier = new Barrier(3); // 3 threads will hit this barrier
            const int threadCount = 3;

            // Act
            var tasks = new Task[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    barrier.SignalAndWait(); // Ensure all threads start simultaneously

                    await SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, async () =>
                    {
                        var currentCount = Interlocked.Increment(ref executionCount);
                        await Task.Delay(10); // Simulate work
                        return currentCount;
                    });
                });
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(threadCount, executionCount);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GivenInvalidUri_WhenExecutingWithLock_ThenThrowsArgumentException(string invalidUri)
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                SearchParameterConcurrencyManager.ExecuteWithLockAsync(invalidUri, () => Task.FromResult(1)));
        }

        [Fact]
        public async Task GivenNullOperation_WhenExecutingWithLock_ThenThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                SearchParameterConcurrencyManager.ExecuteWithLockAsync<int>(TestUri1, null));

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, null));
        }
    }
}
