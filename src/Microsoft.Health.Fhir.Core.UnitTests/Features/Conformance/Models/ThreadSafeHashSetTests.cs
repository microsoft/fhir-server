// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance.Models
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class ThreadSafeHashSetTests
    {
        [Fact]
        public void GivenANewInstance_WhenCreated_ThenItShouldBeEmpty()
        {
            // Arrange & Act
            var hashSet = new ThreadSafeHashSet<int>();

            // Assert
            Assert.Empty(hashSet);
            Assert.False(hashSet.IsReadOnly);
        }

        [Fact]
        public void GivenAnItem_WhenAdded_ThenItShouldBeInTheSet()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();

            // Act
            hashSet.Add(42);

            // Assert
            Assert.Single(hashSet);
            Assert.Contains(42, hashSet);
        }

        [Fact]
        public void GivenMultipleItems_WhenAdded_ThenAllShouldBeInTheSet()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<string>();
            var items = new[] { "item1", "item2", "item3" };

            // Act
            foreach (var item in items)
            {
                hashSet.Add(item);
            }

            // Assert
            Assert.Equal(3, hashSet.Count);
            foreach (var item in items)
            {
                Assert.Contains(item, hashSet);
            }
        }

        [Fact]
        public void GivenDuplicateItems_WhenAdded_ThenOnlyOneInstanceShouldExist()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();

            // Act
            hashSet.Add(10);
            hashSet.Add(10);
            hashSet.Add(10);

            // Assert
            Assert.Single(hashSet);
        }

        [Fact]
        public void GivenAnExistingItem_WhenRemoved_ThenItShouldNotBeInTheSet()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();
            hashSet.Add(100);

            // Act
            bool removed = hashSet.Remove(100);

            // Assert
            Assert.True(removed);
            Assert.Empty(hashSet);
            Assert.DoesNotContain(100, hashSet);
        }

        [Fact]
        public void GivenANonExistingItem_WhenRemoved_ThenItShouldReturnFalse()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();

            // Act
            bool removed = hashSet.Remove(999);

            // Assert
            Assert.False(removed);
        }

        [Fact]
        public void GivenAPopulatedSet_WhenCleared_ThenItShouldBeEmpty()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();
            hashSet.Add(1);
            hashSet.Add(2);
            hashSet.Add(3);

            // Act
            hashSet.Clear();

            // Assert
            Assert.Empty(hashSet);
        }

        [Fact]
        public void GivenAPopulatedSet_WhenEnumerated_ThenAllItemsShouldBeReturned()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();
            var items = new[] { 1, 2, 3, 4, 5 };
            foreach (var item in items)
            {
                hashSet.Add(item);
            }

            // Act
            var result = hashSet.ToList();

            // Assert
            Assert.Equal(5, result.Count);
            foreach (var item in items)
            {
                Assert.Contains(item, result);
            }
        }

        [Fact]
        public void GivenAPopulatedSet_WhenCopiedToArray_ThenAllItemsShouldBeCopied()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();
            var items = new[] { 10, 20, 30 };
            foreach (var item in items)
            {
                hashSet.Add(item);
            }

            var array = new int[3];

            // Act
            hashSet.CopyTo(array, 0);

            // Assert
            foreach (var item in items)
            {
                Assert.Contains(item, array);
            }
        }

        [Fact]
        public async Task GivenConcurrentAdds_WhenExecuted_ThenAllItemsShouldBeAddedWithoutDataLoss()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();
            const int numberOfThreads = 10;
            const int itemsPerThread = 1000;

            // Act
            var tasks = Enumerable.Range(0, numberOfThreads).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < itemsPerThread; i++)
                    {
                        hashSet.Add((threadId * itemsPerThread) + i);
                    }
                }));

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(numberOfThreads * itemsPerThread, hashSet.Count);
        }

        [Fact]
        public async Task GivenConcurrentAddsOfSameItems_WhenExecuted_ThenNoDuplicatesShouldExist()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();
            const int numberOfThreads = 20;
            const int itemsToAdd = 100;

            // Act - Multiple threads adding the same items
            var tasks = Enumerable.Range(0, numberOfThreads).Select(_ =>
                Task.Run(() =>
                {
                    for (int i = 0; i < itemsToAdd; i++)
                    {
                        hashSet.Add(i);
                    }
                }));

            await Task.WhenAll(tasks);

            // Assert - Should only have unique items
            Assert.Equal(itemsToAdd, hashSet.Count);
        }

        [Fact]
        public async Task GivenConcurrentRemoves_WhenExecuted_ThenItemsShouldBeRemovedCorrectly()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();
            const int totalItems = 1000;

            for (int i = 0; i < totalItems; i++)
            {
                hashSet.Add(i);
            }

            // Act - Remove half the items concurrently
            var tasks = Enumerable.Range(0, totalItems / 2).Select(i =>
                Task.Run(() => hashSet.Remove(i)));

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(totalItems / 2, hashSet.Count);
            for (int i = 0; i < totalItems / 2; i++)
            {
                Assert.DoesNotContain(i, hashSet);
            }

            for (int i = totalItems / 2; i < totalItems; i++)
            {
                Assert.Contains(i, hashSet);
            }
        }

        [Fact]
        public async Task GivenConcurrentAddAndRemove_WhenExecuted_ThenSetShouldMaintainConsistency()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();
            const int operations = 1000;

            // Act - Some threads adding, some threads removing
            var addTasks = Enumerable.Range(0, operations).Select(i =>
                Task.Run(() => hashSet.Add(i)));

            var removeTasks = Enumerable.Range(0, operations / 2).Select(i =>
                Task.Run(() => hashSet.Remove(i)));

            await Task.WhenAll(addTasks.Concat(removeTasks));

            // Assert - At least half the items should remain
            Assert.True(hashSet.Count >= operations / 2);
            Assert.True(hashSet.Count <= operations);
        }

        [Fact]
        public async Task GivenConcurrentContainsChecks_WhenExecuted_ThenShouldReturnCorrectResults()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();
            const int itemsToAdd = 500;

            for (int i = 0; i < itemsToAdd; i++)
            {
                hashSet.Add(i);
            }

            // Act - Multiple threads checking contains
            var tasks = Enumerable.Range(0, itemsToAdd).Select(i =>
                Task.Run(() =>
                {
                    bool exists = hashSet.Contains(i);
                    Assert.True(exists);
                }));

            await Task.WhenAll(tasks);

            // Assert - All checks should have succeeded
            Assert.Equal(itemsToAdd, hashSet.Count);
        }

        [Fact]
        public async Task GivenConcurrentMixedOperations_WhenExecuted_ThenSetShouldRemainThreadSafe()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();
            const int operationsPerType = 500;

            // Act - Mix of add, remove, contains, and enumeration operations
            var addTasks = Enumerable.Range(0, operationsPerType).Select(i =>
                Task.Run(() => hashSet.Add(i)));

            var removeTasks = Enumerable.Range(0, operationsPerType / 4).Select(i =>
                Task.Run(() => hashSet.Remove(i)));

            var containsTasks = Enumerable.Range(0, operationsPerType / 2).Select(i =>
                Task.Run(() => hashSet.Contains(i)));

            var enumerationTasks = Enumerable.Range(0, 10).Select(_ =>
                Task.Run(() =>
                {
                    var snapshot = hashSet.ToList();
                    return snapshot.Count;
                }));

            var allTasks = addTasks
                .Concat(removeTasks)
                .Concat(containsTasks)
                .Concat(enumerationTasks);

            await Task.WhenAll(allTasks);

            // Assert - No exceptions should be thrown and set should be in valid state
            Assert.True(hashSet.Count >= 0);
            Assert.True(hashSet.Count <= operationsPerType);
        }

        [Fact]
        public async Task GivenConcurrentClearAndAdd_WhenExecuted_ThenSetShouldHandleCorrectly()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();
            const int iterations = 100;

            // Act - One thread clearing, others adding
            var clearTask = Task.Run(async () =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    await Task.Delay(1);
                    hashSet.Clear();
                }
            });

            var addTasks = Enumerable.Range(0, 10).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < iterations * 10; i++)
                    {
                        hashSet.Add((threadId * 1000) + i);
                    }
                }));

            await Task.WhenAll(addTasks.Concat(new[] { clearTask }));

            // Assert - No exceptions and valid state
            Assert.True(hashSet.Count >= 0);
        }

        [Fact]
        public async Task GivenHighContentionScenario_WhenExecuted_ThenNoDeadlocksShouldOccur()
        {
            // Arrange
            var hashSet = new ThreadSafeHashSet<int>();
            const int numberOfThreads = 100;
            const int operationsPerThread = 1000;

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

            // Act - High contention scenario with many threads
            Task[] tasks = new Task[numberOfThreads];
            for (int i = 0; i < numberOfThreads; i++)
            {
                tasks[i] = Task.Run(
                () =>
                {
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        int threadId = Task.CurrentId ?? 1;
                        int value = (threadId * operationsPerThread) + i;
                        hashSet.Add(value);
                        hashSet.Contains(value);
                        if (i % 2 == 0)
                        {
                            hashSet.Remove(value);
                        }
                    }
                },
                cancellationTokenSource.Token);
            }

#pragma warning disable xUnit1031 // Justification: using timeout to detect potential deadlocks.
            await Task.WhenAll(tasks);
#pragma warning restore xUnit1031
            int totalTasksCompletedSuccessFully = tasks.Count(t => t.IsCompletedSuccessfully);

            // Assert - All operations should complete without deadlock
            Assert.True(totalTasksCompletedSuccessFully == numberOfThreads, $"Total completed tasks is different than the expected. Total tasks: {numberOfThreads}. Completed tasks: {totalTasksCompletedSuccessFully}.");
            Assert.True(hashSet.Count >= 0);
        }
    }
}
