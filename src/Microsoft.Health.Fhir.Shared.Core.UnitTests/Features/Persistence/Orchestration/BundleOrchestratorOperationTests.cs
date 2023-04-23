// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Persistence.Orchestration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    [Trait(Traits.Category, Categories.BundleOrchestrator)]
    public class BundleOrchestratorOperationTests
    {
        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(500)]
        public async Task GivenABatchOperation_WhenAppendedMultipleResourcesInSequenceWaitForAllToBeAppended_ThenCompleteWithSuccess(int numberOfResources)
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            Func<IFhirDataStore> newDataStoreFunc = () =>
            {
                return Substitute.For<IFhirDataStore>();
            };

            var batchOrchestrator = new BundleOrchestrator(isEnabled: true, createDataStoreFunc: newDataStoreFunc);
            IBundleOrchestratorOperation operation = batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, "PUT", numberOfResources);

            Assert.Equal(BundleOrchestratorOperationStatus.Open, operation.Status);

            // Append resources to an operation.
            Task[] tasksWaitingForMergeAsync = new Task[numberOfResources];
            for (int i = 0; i < numberOfResources; i++)
            {
                DomainResource resource = BundleTestsCommonFunctions.GetSamplePatient(Guid.NewGuid());
                ResourceWrapper resourceWrapper = await BundleTestsCommonFunctions.GetResourceWrapperAsync(resource);

                Task appendedResourceTask = operation.AppendResourceAsync(resourceWrapper, cts.Token);
                tasksWaitingForMergeAsync[i] = appendedResourceTask;

                if (i == (numberOfResources - 1))
                {
                    Task.WaitAll(tasksWaitingForMergeAsync);

                    // After waiting for all tasks, the operation should be completed.
                    Assert.Equal(BundleOrchestratorOperationStatus.Completed, operation.Status);
                }
                else
                {
                    Assert.Equal(BundleOrchestratorOperationStatus.WaitingForResources, operation.Status);
                }
            }

            Assert.Equal(numberOfResources, operation.OriginalExpectedNumberOfResources);
            Assert.Equal(numberOfResources, operation.CurrentExpectedNumberOfResources);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(500)]
        public void GivenABatchOperation_WhenAppendedMultipleResourcesInParallelWaitForAllToBeAppended_ThenCompleteWithSuccess(int numberOfResources)
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            Func<IFhirDataStore> newDataStoreFunc = () =>
            {
                return Substitute.For<IFhirDataStore>();
            };
            var batchOrchestrator = new BundleOrchestrator(isEnabled: true, createDataStoreFunc: newDataStoreFunc);

            IBundleOrchestratorOperation operation = batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, "POST", numberOfResources);

            Assert.Equal(BundleOrchestratorOperationStatus.Open, operation.Status);

            // Append resources to an operation.
            List<Task> tasksWaitingForMergeAsync = new List<Task>(capacity: numberOfResources);
            Parallel.For(0, numberOfResources, async (i, task) =>
            {
                DomainResource resource = BundleTestsCommonFunctions.GetSamplePatient(Guid.NewGuid());
                ResourceWrapper resourceWrapper = await BundleTestsCommonFunctions.GetResourceWrapperAsync(resource);

                Task appendedResourceTask = operation.AppendResourceAsync(resourceWrapper, cts.Token);
                tasksWaitingForMergeAsync.Add(appendedResourceTask);
            });

            Task.WaitAll(tasksWaitingForMergeAsync.ToArray());

            Assert.Equal(BundleOrchestratorOperationStatus.Completed, operation.Status);
            Assert.Equal(numberOfResources, operation.OriginalExpectedNumberOfResources);
            Assert.Equal(numberOfResources, operation.CurrentExpectedNumberOfResources);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(500)]
        public void GivenABatchOperation_WhenAllResourcedAreReleasedInParallel_ThenCancelTheOperation(int numberOfResources)
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            Func<IFhirDataStore> newDataStoreFunc = () =>
            {
                return Substitute.For<IFhirDataStore>();
            };
            var batchOrchestrator = new BundleOrchestrator(isEnabled: true, createDataStoreFunc: newDataStoreFunc);

            IBundleOrchestratorOperation operation = batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, "POST", numberOfResources);

            Assert.Equal(BundleOrchestratorOperationStatus.Open, operation.Status);

            // Append resources to an operation.
            List<Task> tasksWaitingForMergeAsync = new List<Task>(capacity: numberOfResources);
            Parallel.For(0, numberOfResources, (i, task) =>
            {
                Task releasedResourceTask = operation.ReleaseResourceAsync("Canceled due tests.", cts.Token);
                tasksWaitingForMergeAsync.Add(releasedResourceTask);
            });

            Task.WaitAll(tasksWaitingForMergeAsync.ToArray());

            Assert.Equal(BundleOrchestratorOperationStatus.Canceled, operation.Status);
            Assert.Equal(numberOfResources, operation.OriginalExpectedNumberOfResources);
            Assert.Equal(0, operation.CurrentExpectedNumberOfResources);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(500)]
        public void GivenABatchOperation_WhenHalfOfResourcesAreReleasedInParallel_ThenBatchShouldProcessTheRemainingResources(int numberOfResources)
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            Func<IFhirDataStore> newDataStoreFunc = () =>
            {
                return Substitute.For<IFhirDataStore>();
            };
            var batchOrchestrator = new BundleOrchestrator(isEnabled: true, createDataStoreFunc: newDataStoreFunc);

            IBundleOrchestratorOperation operation = batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, "POST", numberOfResources);

            Assert.Equal(BundleOrchestratorOperationStatus.Open, operation.Status);

            // Append resources to an operation.
            List<Task> tasksWaitingForMergeAsync = new List<Task>(capacity: numberOfResources);
            Parallel.For(0, numberOfResources, async (i, task) =>
            {
                Task appendTask;
                if (i % 2 == 0)
                {
                    appendTask = operation.ReleaseResourceAsync("Canceled due tests.", cts.Token);
                }
                else
                {
                    DomainResource resource = BundleTestsCommonFunctions.GetSamplePatient(Guid.NewGuid());
                    ResourceWrapper resourceWrapper = await BundleTestsCommonFunctions.GetResourceWrapperAsync(resource);

                    appendTask = operation.AppendResourceAsync(resourceWrapper, cts.Token);
                }

                tasksWaitingForMergeAsync.Add(appendTask);
            });

            Task.WaitAll(tasksWaitingForMergeAsync.ToArray());

            Assert.Equal(BundleOrchestratorOperationStatus.Completed, operation.Status);
            Assert.Equal(numberOfResources, operation.OriginalExpectedNumberOfResources);
            Assert.Equal(numberOfResources / 2, operation.CurrentExpectedNumberOfResources);
        }

        [Fact]
        public async Task GivenABatchOperation_WhenAppendedOnlyOneOutOfAllSupposedResourcesIsAppended_ThenThrowATaskCanceledOperationDueTimeout()
        {
            const int numberOfResources = 10;

            // Short cancellation time. This test should fail fast and throw a TaskCanceledException.
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            Func<IFhirDataStore> newDataStoreFunc = () =>
            {
                return Substitute.For<IFhirDataStore>();
            };
            var batchOrchestrator = new BundleOrchestrator(isEnabled: true, createDataStoreFunc: newDataStoreFunc);

            IBundleOrchestratorOperation operation = batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, "POST", expectedNumberOfResources: numberOfResources);

            Assert.Equal(BundleOrchestratorOperationStatus.Open, operation.Status);

            // Append resources to an operation.
            List<Task> tasksWaitingForMergeAsync = new List<Task>(capacity: numberOfResources);

            DomainResource resource = BundleTestsCommonFunctions.GetSamplePatient(Guid.NewGuid());
            ResourceWrapper resourceWrapper = await BundleTestsCommonFunctions.GetResourceWrapperAsync(resource);

            // A single resource will be appended to this operation.
            // In this test, we are forcing the operation to timeout while waiting for the remain resources.
            tasksWaitingForMergeAsync.Add(operation.AppendResourceAsync(resourceWrapper, cts.Token));

            AggregateException age = Assert.Throws<AggregateException>(() => Task.WaitAll(tasksWaitingForMergeAsync.ToArray()));

            Assert.True(age.InnerException is TaskCanceledException);
            Assert.Equal(BundleOrchestratorOperationStatus.Canceled, operation.Status);
            Assert.Equal(numberOfResources, operation.OriginalExpectedNumberOfResources);
            Assert.Equal(numberOfResources, operation.CurrentExpectedNumberOfResources);
        }
    }
}
