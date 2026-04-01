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
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance.Models
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class ThreadSafeListTests
    {
        [Fact]
        public void GivenASetOfRegularBasicOperations_WhenExecuted_ThenTheyShouldBehaveAsExpected()
        {
            // Create a new instance of ThreadSafeList
            var list = new ThreadSafeList<string>();
            Assert.Empty(list);
            Assert.False(list.IsReadOnly);
            Assert.True(list.Count == 0, "Count should be accessible at this moment.");

            // Add elements to the collection.
            list.Add("test1");
            list.Add("test2");
            list.Add("test3");
            Assert.Equal(3, list.Count);

            // Check if elements are in the collection, as they should.
            Assert.Contains("test1", list);
            Assert.Contains("test2", list);
            Assert.Contains("test3", list);
            Assert.DoesNotContain("test4", list);
            Assert.Equal(3, list.Count);

            // Testing removing operations.
            bool removed = list.Remove("test2");
            Assert.True(removed);
            Assert.Equal(2, list.Count);
            Assert.DoesNotContain("test2", list);
            removed = list.Remove("test4");
            Assert.False(removed);

            // After cleared, no elements should be present.
            list.Clear();
            Assert.Empty(list);
            Assert.True(list.Count == 0, "Count should be accessible at this moment.");

            // Duplicate elements are allowed (unlike ThreadSafeHashSet).
            list.Add("test1");
            list.Add("test1");
            Assert.Equal(2, list.Count);
        }

        [Fact]
        public void GivenANewInstance_WhenCreated_ThenItShouldBeEmpty()
        {
            // Arrange & Act
            var list = new ThreadSafeList<int>();

            // Assert
            Assert.Empty(list);
            Assert.False(list.IsReadOnly);
        }

        [Fact]
        public void GivenAnItem_WhenAdded_ThenItShouldBeInTheList()
        {
            // Arrange
            var list = new ThreadSafeList<int>();

            // Act
            list.Add(42);

            // Assert
            Assert.Single(list);
            Assert.Contains(42, list);
        }

        [Fact]
        public void GivenMultipleItems_WhenAdded_ThenAllShouldBeInTheList()
        {
            // Arrange
            var list = new ThreadSafeList<string>();
            var items = new[] { "item1", "item2", "item3" };

            // Act
            foreach (var item in items)
            {
                list.Add(item);
            }

            // Assert
            Assert.Equal(3, list.Count);
            foreach (var item in items)
            {
                Assert.Contains(item, list);
            }
        }

        [Fact]
        public void GivenDuplicateItems_WhenAdded_ThenAllInstancesShouldExist()
        {
            // Arrange
            var list = new ThreadSafeList<int>();

            // Act
            list.Add(10);
            list.Add(10);
            list.Add(10);

            // Assert
            Assert.Equal(3, list.Count);
        }

        [Fact]
        public void GivenAnExistingItem_WhenRemoved_ThenItShouldNotBeInTheList()
        {
            // Arrange
            var list = new ThreadSafeList<int>();
            list.Add(100);

            // Act
            bool removed = list.Remove(100);

            // Assert
            Assert.True(removed);
            Assert.Empty(list);
            Assert.DoesNotContain(100, list);
        }

        [Fact]
        public void GivenANonExistingItem_WhenRemoved_ThenItShouldReturnFalse()
        {
            // Arrange
            var list = new ThreadSafeList<int>();

            // Act
            bool removed = list.Remove(999);

            // Assert
            Assert.False(removed);
        }

        [Fact]
        public void GivenDuplicateItems_WhenOneIsRemoved_ThenOnlyFirstOccurrenceShouldBeRemoved()
        {
            // Arrange
            var list = new ThreadSafeList<int>();
            list.Add(10);
            list.Add(10);
            list.Add(10);

            // Act
            bool removed = list.Remove(10);

            // Assert
            Assert.True(removed);
            Assert.Equal(2, list.Count);
        }

        [Fact]
        public void GivenAPopulatedList_WhenCleared_ThenItShouldBeEmpty()
        {
            // Arrange
            var list = new ThreadSafeList<int>();
            list.Add(1);
            list.Add(2);
            list.Add(3);

            // Act
            list.Clear();

            // Assert
            Assert.Empty(list);
        }

        [Fact]
        public void GivenAPopulatedList_WhenEnumerated_ThenAllItemsShouldBeReturned()
        {
            // Arrange
            var list = new ThreadSafeList<int>();
            var items = new[] { 1, 2, 3, 4, 5 };
            foreach (var item in items)
            {
                list.Add(item);
            }

            // Act
            var result = list.ToList();

            // Assert
            Assert.Equal(5, result.Count);
            foreach (var item in items)
            {
                Assert.Contains(item, result);
            }
        }

        [Fact]
        public void GivenAPopulatedList_WhenCopiedToArray_ThenAllItemsShouldBeCopied()
        {
            // Arrange
            var list = new ThreadSafeList<int>();
            var items = new[] { 10, 20, 30 };
            foreach (var item in items)
            {
                list.Add(item);
            }

            var array = new int[3];

            // Act
            list.CopyTo(array, 0);

            // Assert
            foreach (var item in items)
            {
                Assert.Contains(item, array);
            }
        }

        [Fact]
        public void GivenAPopulatedList_WhenCopiedToArrayWithOffset_ThenItemsShouldBeCopiedAtCorrectPosition()
        {
            // Arrange
            var list = new ThreadSafeList<int>();
            list.Add(10);
            list.Add(20);

            var array = new int[5];

            // Act
            list.CopyTo(array, 2);

            // Assert
            Assert.Equal(0, array[0]);
            Assert.Equal(0, array[1]);
            Assert.True(array.Skip(2).Take(2).OrderBy(x => x).SequenceEqual(new[] { 10, 20 }));
        }

        [Fact]
        public void GivenAnItemNotInList_WhenContainsCalled_ThenShouldReturnFalse()
        {
            // Arrange
            var list = new ThreadSafeList<string>();
            list.Add("exists");

            // Act & Assert
            Assert.Single(list);
            Assert.Contains("exists", list);
            Assert.DoesNotContain("does_not_exist", list);
        }

        [Fact]
        public void GivenConcurrentAdds_WhenExecuted_ThenAllItemsShouldBeAddedWithoutDataLoss()
        {
            // Arrange
            var list = new ThreadSafeList<int>();
            const int numberOfThreads = 10;
            const int itemsPerThread = 1000;

            // Act
            var tasks = Enumerable.Range(0, numberOfThreads).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < itemsPerThread; i++)
                    {
                        list.Add((threadId * itemsPerThread) + i);
                    }
                })).ToArray();

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(tasks, cancellationTokenSource.Token);
#pragma warning restore xUnit1031

            // Assert
            Assert.Equal(numberOfThreads * itemsPerThread, list.Count);
        }

        [Fact]
        public void GivenConcurrentAddsOfSameItems_WhenExecuted_ThenAllDuplicatesShouldExist()
        {
            // Arrange
            var list = new ThreadSafeList<int>();
            const int numberOfThreads = 20;
            const int itemsToAdd = 100;

            // Act - Multiple threads adding the same items
            var tasks = Enumerable.Range(0, numberOfThreads).Select(_ =>
                Task.Run(() =>
                {
                    for (int i = 0; i < itemsToAdd; i++)
                    {
                        list.Add(i);
                    }
                })).ToArray();

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(tasks, cancellationTokenSource.Token);
#pragma warning restore xUnit1031

            // Assert - Unlike a hash set, duplicates are expected
            Assert.Equal(numberOfThreads * itemsToAdd, list.Count);
        }

        [Fact]
        public void GivenConcurrentContainsChecks_WhenExecuted_ThenShouldReturnCorrectResults()
        {
            // Arrange
            var list = new ThreadSafeList<int>();
            const int itemsToAdd = 500;

            for (int i = 0; i < itemsToAdd; i++)
            {
                list.Add(i);
            }

            // Act - Multiple threads checking contains
            var tasks = Enumerable.Range(0, itemsToAdd).Select(i =>
                Task.Run(() =>
                {
                    bool exists = list.Contains(i);
                    Assert.True(exists);
                })).ToArray();

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(tasks, cancellationTokenSource.Token);
#pragma warning restore xUnit1031

            // Assert - All checks should have succeeded
            Assert.Equal(itemsToAdd, list.Count);
        }

        [Fact]
        public void GivenConcurrentRemoves_WhenExecuted_ThenItemsShouldBeRemovedCorrectly()
        {
            // Arrange
            var list = new ThreadSafeList<int>();
            const int totalItems = 1000;

            for (int i = 0; i < totalItems; i++)
            {
                list.Add(i);
            }

            // Act - Remove half the items concurrently
            var tasks = Enumerable.Range(0, totalItems / 2).Select(i =>
                Task.Run(() => list.Remove(i))).ToArray();

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(tasks, cancellationTokenSource.Token);
#pragma warning restore xUnit1031

            // Assert
            Assert.Equal(totalItems / 2, list.Count);
            for (int i = 0; i < totalItems / 2; i++)
            {
                Assert.DoesNotContain(i, list);
            }

            for (int i = totalItems / 2; i < totalItems; i++)
            {
                Assert.Contains(i, list);
            }
        }

        [Fact]
        public void GivenConcurrentAddAndRemove_WhenExecuted_ThenListShouldMaintainConsistency()
        {
            // Arrange
            var list = new ThreadSafeList<int>();
            const int operations = 1000;

            // Act - Some threads adding, some threads removing
            var addTasks = Enumerable.Range(0, operations).Select(i =>
                Task.Run(() => list.Add(i)));

            var removeTasks = Enumerable.Range(0, operations / 2).Select(i =>
                Task.Run(() => list.Remove(i)));

            var allTasks = addTasks.Concat(removeTasks).ToArray();

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(allTasks, cancellationTokenSource.Token);
#pragma warning restore xUnit1031

            // Assert - At least half the items should remain
            Assert.True(list.Count >= operations / 2);
            Assert.True(list.Count <= operations);
        }

        [Fact]
        public void GivenConcurrentMixedOperations_WhenExecuted_ThenListShouldRemainThreadSafe()
        {
            // Arrange
            var list = new ThreadSafeList<int>();
            const int operationsPerType = 500;

            // Act - Mix of add, remove, contains, and enumeration operations
            var addTasks = Enumerable.Range(0, operationsPerType).Select(i =>
                Task.Run(() => list.Add(i)));

            var removeTasks = Enumerable.Range(0, operationsPerType / 4).Select(i =>
                Task.Run(() => list.Remove(i)));

            var containsTasks = Enumerable.Range(0, operationsPerType / 2).Select(i =>
                Task.Run(() => list.Contains(i)));

            var enumerationTasks = Enumerable.Range(0, 10).Select(_ =>
                Task.Run(() =>
                {
                    var snapshot = list.ToList();
                    return snapshot.Count;
                }));

            var allTasks = addTasks
                .Concat(removeTasks)
                .Concat(containsTasks)
                .Concat(enumerationTasks)
                .ToArray();

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(allTasks, cancellationTokenSource.Token);
#pragma warning restore xUnit1031

            // Assert - No exceptions should be thrown and list should be in valid state
            Assert.True(list.Count >= 0);
            Assert.True(list.Count <= operationsPerType);
        }

        [Fact]
        public void GivenConcurrentClearAndAdd_WhenExecuted_ThenListShouldHandleCorrectly()
        {
            // Arrange
            var list = new ThreadSafeHashSet<int>();
            const int iterations = 100;

            // Act - One thread clearing, others adding
            var clearTask = Task.Run(async () =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    await Task.Delay(1);
                    list.Clear();
                }
            });

            List<Task> tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < iterations * 10; i++)
                    {
                        list.Add((i * 1000) + i);
                    }

                    try
                    {
                        foreach (var item in list)
                        {
                            // Testing if iterating through the list will raise exceptions like 'Collection was modified; enumeration operation may not execute'.
                        }
                    }
                    catch (Exception e)
                    {
                        Assert.Fail($"Error while iterating through items in the list. Exception: {e.Message}");
                    }
                }));
            }

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(tasks.ToArray(), cancellationTokenSource.Token);
#pragma warning restore xUnit1031
        }

        [Fact]
        public void GivenHighContentionScenario_WhenExecuted_ThenNoDeadlocksShouldOccur()
        {
            // Multiple threads trying to add elements to the thread-safe list while others are enumerating, simulating a high contention scenario.
            var list = new ThreadSafeList<int>();
            const int numberOfThreads = 100;
            const int operationsPerThread = 1000;

            // Act - High contention scenario with many threads
            Task[] tasks = new Task[numberOfThreads];

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

            for (int i = 0; i < numberOfThreads; i++)
            {
                tasks[i] = Task.Run(
                () =>
                {
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        int threadId = Task.CurrentId ?? 1;
                        int value = (threadId * operationsPerThread) + i;
                        list.Add(value);
                        list.Contains(value);
                        if (i % 2 == 0)
                        {
                            list.Remove(value);
                        }
                    }
                },
                cancellationTokenSource.Token);
            }

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(tasks, cancellationTokenSource.Token);
#pragma warning restore xUnit1031
            int totalTasksCompletedSuccessFully = tasks.Count(t => t.IsCompletedSuccessfully);

            // Assert - All operations should complete without deadlock
            Assert.True(totalTasksCompletedSuccessFully == numberOfThreads, $"Total completed tasks is different than the expected. Total tasks: {numberOfThreads}. Completed tasks: {totalTasksCompletedSuccessFully}.");
            Assert.True(list.Count >= 0);
        }
    }
}
