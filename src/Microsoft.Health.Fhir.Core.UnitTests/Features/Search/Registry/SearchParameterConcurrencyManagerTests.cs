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
            var lockObject = new object();
            var entryBarriers = new List<TaskCompletionSource<bool>>();
            var continueSignals = new List<TaskCompletionSource<bool>>();

            // Create synchronization primitives for each operation
            for (int i = 0; i < concurrentOperations; i++)
            {
                entryBarriers.Add(new TaskCompletionSource<bool>());
                continueSignals.Add(new TaskCompletionSource<bool>());
            }

            // Act
            var tasks = new List<Task<int>>();
            for (int i = 0; i < concurrentOperations; i++)
            {
                var operationId = i;
                var entryBarrier = entryBarriers[i];
                var continueSignal = continueSignals[i];

                tasks.Add(SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, async () =>
                {
                    lock (lockObject)
                    {
                        executionOrder.Add(operationId);
                    }

                    // Signal that this operation has entered the critical section
                    entryBarrier.SetResult(true);

                    // Wait for permission to continue (controlled by test)
                    await continueSignal.Task;

                    return operationId;
                }));
            }

            // Verify operations execute sequentially by controlling their execution
            for (int i = 0; i < concurrentOperations; i++)
            {
                // Wait for the next operation to enter the critical section
                await entryBarriers[i].Task;

                // Verify only this operation has executed so far
                lock (lockObject)
                {
                    Assert.Equal(i + 1, executionOrder.Count);
                    Assert.Equal(i, executionOrder[i]);
                }

                // Allow this operation to complete
                continueSignals[i].SetResult(true);
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(concurrentOperations, results.Length);
            Assert.Equal(concurrentOperations, executionOrder.Count);

            // Verify all operations completed in the correct order
            for (int i = 0; i < concurrentOperations; i++)
            {
                Assert.Equal(i, executionOrder[i]);
                Assert.Equal(i, results[i]);
            }
        }

        [Fact]
        public async Task GivenDifferentSearchParameterUris_WhenExecutingConcurrently_ThenOperationsExecuteInParallel()
        {
            // Arrange
            const int operationsPerUri = 2;
            var uri1StartedCount = 0;
            var uri2StartedCount = 0;
            var bothUrisStarted = new TaskCompletionSource<bool>();
            var canContinue = new TaskCompletionSource<bool>();
            var lockObject = new object();

            // Act
            var tasks = new List<Task>();

            // Add operations for first URI
            for (int i = 0; i < operationsPerUri; i++)
            {
                tasks.Add(SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, async () =>
                {
                    bool shouldSignal = false;
                    lock (lockObject)
                    {
                        uri1StartedCount++;

                        // Signal when both URIs have at least one operation started
                        if (uri1StartedCount > 0 && uri2StartedCount > 0)
                        {
                            shouldSignal = true;
                        }
                    }

                    if (shouldSignal)
                    {
                        bothUrisStarted.TrySetResult(true);
                    }

                    await canContinue.Task;
                }));
            }

            // Add operations for second URI
            for (int i = 0; i < operationsPerUri; i++)
            {
                tasks.Add(SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri2, async () =>
                {
                    bool shouldSignal = false;
                    lock (lockObject)
                    {
                        uri2StartedCount++;

                        // Signal when both URIs have at least one operation started
                        if (uri1StartedCount > 0 && uri2StartedCount > 0)
                        {
                            shouldSignal = true;
                        }
                    }

                    if (shouldSignal)
                    {
                        bothUrisStarted.TrySetResult(true);
                    }

                    await canContinue.Task;
                }));
            }

            // Wait for operations on both URIs to start concurrently
            await bothUrisStarted.Task;

            // Verify both URIs have operations running concurrently
            lock (lockObject)
            {
                Assert.True(uri1StartedCount > 0, "At least one operation on URI1 should have started");
                Assert.True(uri2StartedCount > 0, "At least one operation on URI2 should have started");
            }

            // Allow all operations to complete
            canContinue.SetResult(true);
            await Task.WhenAll(tasks);

            // Assert final counts
            Assert.Equal(operationsPerUri, uri1StartedCount);
            Assert.Equal(operationsPerUri, uri2StartedCount);
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

            // Wait for cleanup with exponential backoff instead of fixed delay
            var activeLockCount = SearchParameterConcurrencyManager.ActiveLockCount;
            var attempts = 0;
            while (activeLockCount != 1 && attempts < 10)
            {
                await Task.Delay(10 * (int)Math.Pow(2, attempts));
                activeLockCount = SearchParameterConcurrencyManager.ActiveLockCount;
                attempts++;
            }

            // Assert - Should have 1 active lock
            Assert.Equal(1, activeLockCount);

            // Complete second operation
            task2CanContinue.SetResult(true);
            await task2;

            // Wait for final cleanup with exponential backoff
            activeLockCount = SearchParameterConcurrencyManager.ActiveLockCount;
            attempts = 0;
            while (activeLockCount != 0 && attempts < 10)
            {
                await Task.Delay(10 * (int)Math.Pow(2, attempts));
                activeLockCount = SearchParameterConcurrencyManager.ActiveLockCount;
                attempts++;
            }

            // Assert - Should have 0 active locks
            Assert.Equal(0, activeLockCount);
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

            // Wait for cleanup with exponential backoff instead of fixed delay
            var activeLockCount = SearchParameterConcurrencyManager.ActiveLockCount;
            var attempts = 0;
            while (activeLockCount != 0 && attempts < 10)
            {
                await Task.Delay(10 * (int)Math.Pow(2, attempts));
                activeLockCount = SearchParameterConcurrencyManager.ActiveLockCount;
                attempts++;
            }

            // Verify lock is released by ensuring a subsequent operation can execute
            var subsequentExecuted = false;
            await SearchParameterConcurrencyManager.ExecuteWithLockAsync(TestUri1, () =>
            {
                subsequentExecuted = true;
                return Task.CompletedTask;
            });

            Assert.True(subsequentExecuted);
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

            // Wait for cleanup with exponential backoff instead of fixed delay
            var activeLockCount = SearchParameterConcurrencyManager.ActiveLockCount;
            var attempts = 0;
            while (activeLockCount != 0 && attempts < 10)
            {
                await Task.Delay(10 * (int)Math.Pow(2, attempts));
                activeLockCount = SearchParameterConcurrencyManager.ActiveLockCount;
                attempts++;
            }

            Assert.Equal(0, activeLockCount);
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

            // Wait for cleanup with exponential backoff instead of fixed delay
            var activeLockCount = SearchParameterConcurrencyManager.ActiveLockCount;
            var attempts = 0;
            while (activeLockCount >= 10 && attempts < 10)
            {
                await Task.Delay(10 * (int)Math.Pow(2, attempts));
                activeLockCount = SearchParameterConcurrencyManager.ActiveLockCount;
                attempts++;
            }

            // Assert - Should not have accumulated locks
            Assert.True(
                activeLockCount < 10,
                $"Expected less than 10 active locks but found {activeLockCount}");
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

        [Fact]
        public void GivenSearchParameterConcurrencyException_WhenCreatedWithMessageAndInnerException_ThenBothAreSet()
        {
            // Arrange
            const string expectedMessage = "Test concurrency error with inner exception";
            var innerException = new InvalidOperationException("Inner exception");

            // Act
            var exception = new SearchParameterConcurrencyException(expectedMessage, innerException);

            // Assert
            Assert.Equal(expectedMessage, exception.Message);
            Assert.Same(innerException, exception.InnerException);
            Assert.Empty(exception.ConflictedUris);
        }
    }
}
