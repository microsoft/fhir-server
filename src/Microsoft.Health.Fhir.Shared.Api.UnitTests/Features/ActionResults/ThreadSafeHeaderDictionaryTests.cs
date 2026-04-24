// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.ActionResults
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class ThreadSafeHeaderDictionaryTests
    {
        [Fact]
        public void GivenANewInstance_WhenCreated_ThenItShouldBeEmpty()
        {
            // Arrange & Act
            var headers = new ThreadSafeHeaderDictionary();

            // Assert
            Assert.Empty(headers);
            Assert.Equal(0, headers.Count);
            Assert.False(headers.IsReadOnly);
        }

        [Fact]
        public void GivenAHeader_WhenSetViaIndexer_ThenItShouldBeRetrievable()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();

            // Act
            headers["Content-Type"] = "application/json";

            // Assert
            Assert.Equal("application/json", headers["Content-Type"].ToString());
            Assert.Single(headers);
        }

        [Fact]
        public void GivenAMissingKey_WhenAccessedViaIndexer_ThenItShouldReturnEmpty()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();

            // Act
            StringValues result = headers["NonExistent"];

            // Assert
            Assert.Equal(StringValues.Empty, result);
        }

        [Fact]
        public void GivenAnEmptyValue_WhenSetViaIndexer_ThenTheKeyShouldBeRemoved()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            headers["X-Custom"] = "value";
            Assert.Single(headers);

            // Act
            headers["X-Custom"] = StringValues.Empty;

            // Assert
            Assert.Empty(headers);
            Assert.False(headers.ContainsKey("X-Custom"));
        }

        [Fact]
        public void GivenHeaders_WhenAccessedCaseInsensitively_ThenTheyShouldMatch()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            headers["Content-Type"] = "application/json";

            // Act & Assert
            Assert.Equal("application/json", headers["content-type"].ToString());
            Assert.Equal("application/json", headers["CONTENT-TYPE"].ToString());
            Assert.True(headers.ContainsKey("content-type"));
        }

        [Fact]
        public void GivenAContentLength_WhenSet_ThenItShouldBeRetrievable()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();

            // Act
            headers.ContentLength = 1024;

            // Assert
            Assert.Equal(1024, headers.ContentLength);
            Assert.Equal("1024", headers["Content-Length"].ToString());
        }

        [Fact]
        public void GivenNoContentLength_WhenAccessed_ThenItShouldReturnNull()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();

            // Act & Assert
            Assert.Null(headers.ContentLength);
        }

        [Fact]
        public void GivenAContentLength_WhenSetToNull_ThenTheHeaderShouldBeRemoved()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            headers.ContentLength = 512;

            // Act
            headers.ContentLength = null;

            // Assert
            Assert.Null(headers.ContentLength);
            Assert.False(headers.ContainsKey("Content-Length"));
        }

        [Fact]
        public void GivenAHeader_WhenAddedViaAddMethod_ThenItShouldBePresent()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();

            // Act
            headers.Add("X-Request-Id", new StringValues("abc-123"));

            // Assert
            Assert.Equal("abc-123", headers["X-Request-Id"].ToString());
        }

        [Fact]
        public void GivenADuplicateKey_WhenAddedViaAddMethod_ThenItShouldThrowArgumentException()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            headers.Add("X-Request-Id", "abc-123");

            // Act & Assert
            Assert.Throws<ArgumentException>(() => headers.Add("X-Request-Id", "def-456"));
        }

        [Fact]
        public void GivenAKeyValuePair_WhenAddedViaAddMethod_ThenItShouldBePresent()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            var item = new KeyValuePair<string, StringValues>("X-Custom", "value");

            // Act
            headers.Add(item);

            // Assert
            Assert.True(headers.ContainsKey("X-Custom"));
            Assert.Equal("value", headers["X-Custom"].ToString());
        }

        [Fact]
        public void GivenAPopulatedDictionary_WhenRemoved_ThenTheKeyShouldBeGone()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            headers["X-Custom"] = "value";

            // Act
            bool removed = headers.Remove("X-Custom");

            // Assert
            Assert.True(removed);
            Assert.Empty(headers);
        }

        [Fact]
        public void GivenANonExistingKey_WhenRemoved_ThenItShouldReturnFalse()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();

            // Act
            bool removed = headers.Remove("NonExistent");

            // Assert
            Assert.False(removed);
        }

        [Fact]
        public void GivenAMatchingKeyValuePair_WhenRemoved_ThenItShouldBeRemoved()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            headers["X-Custom"] = "value";
            var item = new KeyValuePair<string, StringValues>("X-Custom", "value");

            // Act
            bool removed = headers.Remove(item);

            // Assert
            Assert.True(removed);
            Assert.Empty(headers);
        }

        [Fact]
        public void GivenANonMatchingValuePair_WhenRemoved_ThenItShouldNotBeRemoved()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            headers["X-Custom"] = "value";
            var item = new KeyValuePair<string, StringValues>("X-Custom", "different");

            // Act
            bool removed = headers.Remove(item);

            // Assert
            Assert.False(removed);
            Assert.Single(headers);
        }

        [Fact]
        public void GivenAPopulatedDictionary_WhenCleared_ThenItShouldBeEmpty()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            headers["Header1"] = "val1";
            headers["Header2"] = "val2";
            headers["Header3"] = "val3";

            // Act
            headers.Clear();

            // Assert
            Assert.Empty(headers);
            Assert.Equal(0, headers.Count);
        }

        [Fact]
        public void GivenAPopulatedDictionary_WhenTryGetValueCalled_ThenItShouldReturnCorrectResult()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            headers["X-Custom"] = "value";

            // Act & Assert
            Assert.True(headers.TryGetValue("X-Custom", out StringValues found));
            Assert.Equal("value", found.ToString());
            Assert.False(headers.TryGetValue("Missing", out _));
        }

        [Fact]
        public void GivenAPopulatedDictionary_WhenContainsCalled_ThenItShouldMatchKeyAndValue()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            headers["X-Custom"] = "value";

            // Act & Assert
            Assert.True(headers.Contains(new KeyValuePair<string, StringValues>("X-Custom", "value")));
            Assert.False(headers.Contains(new KeyValuePair<string, StringValues>("X-Custom", "other")));
            Assert.False(headers.Contains(new KeyValuePair<string, StringValues>("Missing", "value")));
        }

        [Fact]
        public void GivenAPopulatedDictionary_WhenKeysAndValuesAccessed_ThenTheyShouldContainAllEntries()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            headers["Header1"] = "val1";
            headers["Header2"] = "val2";

            // Act
            var keys = headers.Keys;
            var values = headers.Values;

            // Assert
            Assert.Equal(2, keys.Count);
            Assert.Equal(2, values.Count);
            Assert.Contains("Header1", keys);
            Assert.Contains("Header2", keys);
        }

        [Fact]
        public void GivenAPopulatedDictionary_WhenEnumerated_ThenAllEntriesShouldBeReturned()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            headers["Header1"] = "val1";
            headers["Header2"] = "val2";
            headers["Header3"] = "val3";

            // Act
            var result = headers.ToList();

            // Assert
            Assert.Equal(3, result.Count);
            Assert.Contains(result, kvp => kvp.Key == "Header1" && kvp.Value == "val1");
            Assert.Contains(result, kvp => kvp.Key == "Header2" && kvp.Value == "val2");
            Assert.Contains(result, kvp => kvp.Key == "Header3" && kvp.Value == "val3");
        }

        [Fact]
        public void GivenAPopulatedDictionary_WhenCopiedToArray_ThenAllEntriesShouldBeCopied()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            headers["Header1"] = "val1";
            headers["Header2"] = "val2";

            var array = new KeyValuePair<string, StringValues>[2];

            // Act
            headers.CopyTo(array, 0);

            // Assert
            Assert.Contains(array, kvp => kvp.Key == "Header1");
            Assert.Contains(array, kvp => kvp.Key == "Header2");
        }

        [Fact]
        public void GivenConcurrentWrites_WhenExecuted_ThenAllHeadersShouldBeAddedWithoutDataLoss()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            const int numberOfThreads = 10;
            const int headersPerThread = 100;

            // Act
            var tasks = Enumerable.Range(0, numberOfThreads).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < headersPerThread; i++)
                    {
                        headers[$"X-Thread{threadId}-Header{i}"] = $"value-{threadId}-{i}";
                    }
                })).ToArray();

            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(tasks, cancellationTokenSource.Token);
#pragma warning restore xUnit1031

            // Assert
            Assert.Equal(numberOfThreads * headersPerThread, headers.Count);
        }

        [Fact]
        public void GivenConcurrentReadsAndWrites_WhenExecuted_ThenNoExceptionsShouldBeThrown()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            const int operationsPerType = 500;

            // Act
            var writeTasks = Enumerable.Range(0, operationsPerType).Select(i =>
                Task.Run(() => headers[$"Header-{i}"] = $"value-{i}"));

            var readTasks = Enumerable.Range(0, operationsPerType).Select(i =>
                Task.Run(() =>
                {
                    _ = headers[$"Header-{i}"];
                    headers.TryGetValue($"Header-{i}", out _);
                    _ = headers.ContainsKey($"Header-{i}");
                }));

            var allTasks = writeTasks.Concat(readTasks).ToArray();

            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(allTasks, cancellationTokenSource.Token);
#pragma warning restore xUnit1031

            // Assert - No exceptions should have been thrown
            Assert.True(headers.Count >= 0);
            Assert.True(headers.Count <= operationsPerType);
        }

        [Fact]
        public void GivenConcurrentEnumerationAndMutation_WhenExecuted_ThenNoExceptionsShouldBeThrown()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            const int iterations = 100;

            // Pre-populate
            for (int i = 0; i < 50; i++)
            {
                headers[$"Header-{i}"] = $"value-{i}";
            }

            // Act - Enumerate while mutating concurrently
            var mutateTasks = Enumerable.Range(0, iterations).Select(i =>
                Task.Run(() =>
                {
                    headers[$"NewHeader-{i}"] = $"new-value-{i}";
                    headers.Remove($"Header-{i % 50}");
                }));

            var enumerateTasks = Enumerable.Range(0, 10).Select(_ =>
                Task.Run(() =>
                {
                    try
                    {
                        foreach (var header in headers)
                        {
                            _ = header.Key;
                            _ = header.Value;
                        }
                    }
                    catch (Exception e)
                    {
                        Assert.Fail($"Enumeration should not throw during concurrent mutation. Exception: {e.Message}");
                    }
                }));

            var allTasks = mutateTasks.Concat(enumerateTasks).ToArray();

            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(allTasks, cancellationTokenSource.Token);
#pragma warning restore xUnit1031

            // Assert - No exceptions should have been thrown
            Assert.True(headers.Count >= 0);
        }

        [Fact]
        public void GivenHighContentionScenario_WhenExecuted_ThenNoDeadlocksShouldOccur()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            const int numberOfThreads = 50;
            const int operationsPerThread = 200;

            // Act
            Task[] tasks = new Task[numberOfThreads];

            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

            for (int t = 0; t < numberOfThreads; t++)
            {
                tasks[t] = Task.Run(
                    () =>
                    {
                        for (int i = 0; i < operationsPerThread; i++)
                        {
                            string key = $"Header-{i % 50}";
                            headers[key] = $"value-{i}";
                            _ = headers[key];
                            headers.ContainsKey(key);

                            if (i % 3 == 0)
                            {
                                headers.Remove(key);
                            }

                            if (i % 5 == 0)
                            {
                                headers.ContentLength = i;
                            }
                        }
                    },
                    cancellationTokenSource.Token);
            }

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(tasks, cancellationTokenSource.Token);
#pragma warning restore xUnit1031

            int totalTasksCompletedSuccessfully = tasks.Count(t => t.IsCompletedSuccessfully);

            // Assert
            Assert.True(
                totalTasksCompletedSuccessfully == numberOfThreads,
                $"Total completed tasks is different than the expected. Total tasks: {numberOfThreads}. Completed tasks: {totalTasksCompletedSuccessfully}.");
            Assert.True(headers.Count >= 0);
        }

        [Fact]
        public void GivenConcurrentClearAndAdd_WhenExecuted_ThenDictionaryShouldHandleCorrectly()
        {
            // Arrange
            var headers = new ThreadSafeHeaderDictionary();
            const int iterations = 100;

            // Act - One thread clearing, others adding
            var clearTask = Task.Run(async () =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    await Task.Delay(1);
                    headers.Clear();
                }
            });

            var addTasks = Enumerable.Range(0, 10).Select(threadId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < iterations * 10; i++)
                    {
                        headers[$"Thread{threadId}-Header{i}"] = $"val-{threadId}-{i}";
                    }

                    try
                    {
                        foreach (var header in headers)
                        {
                            _ = header.Key;
                        }
                    }
                    catch (Exception e)
                    {
                        Assert.Fail($"Enumeration during concurrent clear should not throw. Exception: {e.Message}");
                    }
                }));

            var allTasks = addTasks.Concat(new[] { clearTask }).ToArray();

            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(30000);

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            Task.WaitAll(allTasks, cancellationTokenSource.Token);
#pragma warning restore xUnit1031

            // Assert - No exceptions and valid state
            Assert.True(headers.Count >= 0);
        }
    }
}
