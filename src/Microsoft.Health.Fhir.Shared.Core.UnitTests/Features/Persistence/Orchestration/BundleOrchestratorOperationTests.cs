// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Models;
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
            // When all resources in a bundle are properly appended to the operation sequentially and the operation is committed, then the expected state is 'Completed'.

            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var dataStore = BundleTestsCommonFunctions.GetSubstituteForIFhirDataStore();

            var batchOrchestrator = BundleTestsCommonFunctions.GetBundleOrchestrator();
            IBundleOrchestratorOperation operation = batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, "PUT", numberOfResources);

            Assert.Equal(BundleOrchestratorOperationStatus.Open, operation.Status);

            // Append resources to an operation.
            Task[] tasksWaitingForMergeAsync = new Task[numberOfResources];
            for (int i = 0; i < numberOfResources; i++)
            {
                DomainResource resource = BundleTestsCommonFunctions.GetSamplePatient(Guid.NewGuid());
                ResourceWrapperOperation resourceWrapper = await BundleTestsCommonFunctions.GetResourceWrapperOperationAsync(
                    resource,
                    new BundleResourceContext(Bundle.BundleType.Batch, BundleProcessingLogic.Parallel, GetHttpVerb(i), persistedId: null, operation.Id));

                Task<UpsertOutcome> appendedResourceTask = operation.AppendResourceAsync(resourceWrapper, dataStore, cts.Token);
                tasksWaitingForMergeAsync[i] = appendedResourceTask;

                if (i == (numberOfResources - 1))
                {
                    await Task.WhenAll(tasksWaitingForMergeAsync);

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
        public async Task GivenABatchOperation_WhenAppendedMultipleResourcesInParallelWaitForAllToBeAppended_ThenCompleteWithSuccess(int numberOfResources)
        {
            // When all resources in a bundle are properly appended to the operation in parallel and the operation is committed, then the expected state is 'Completed'.

            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var dataStore = BundleTestsCommonFunctions.GetSubstituteForIFhirDataStore();

            var batchOrchestrator = BundleTestsCommonFunctions.GetBundleOrchestrator();

            IBundleOrchestratorOperation operation = batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, "POST", numberOfResources);

            Assert.Equal(BundleOrchestratorOperationStatus.Open, operation.Status);

            // Append resources to an operation.
            List<Task> tasksWaitingForMergeAsync = new List<Task>(capacity: numberOfResources);
            Parallel.For(0, numberOfResources, async (i, task) =>
            {
                DomainResource resource = BundleTestsCommonFunctions.GetSamplePatient(Guid.NewGuid());
                ResourceWrapperOperation resourceWrapper = await BundleTestsCommonFunctions.GetResourceWrapperOperationAsync(
                    resource,
                    new BundleResourceContext(Bundle.BundleType.Batch, BundleProcessingLogic.Parallel, GetHttpVerb(i), persistedId: null, operation.Id));

                Task<UpsertOutcome> appendedResourceTask = operation.AppendResourceAsync(resourceWrapper, dataStore, cts.Token);
                tasksWaitingForMergeAsync.Add(appendedResourceTask);
            });

            await Task.WhenAll(tasksWaitingForMergeAsync.ToArray());

            Assert.Equal(BundleOrchestratorOperationStatus.Completed, operation.Status);
            Assert.Equal(numberOfResources, operation.OriginalExpectedNumberOfResources);
            Assert.Equal(numberOfResources, operation.CurrentExpectedNumberOfResources);
        }

        [Fact]
        public async Task GivenABatchOperation_WhenJustOneResourcedIsAppendedAndAllOtherResourcesAreHanging_ThenCancelTheOperationAfter120SecondsToAvoidRunningForever()
        {
            // Long running test.

            // A security exit clause in Bundle Orchestrator Operation throws a TaskCanceledException after 100 seconds waiting for resources.
            // This security exit clause was added to avoid a looping running forever, while resources are not appended.

            // In this test, to simulate that scenario, a bundle operation is created expecting 10 resources, but only one is appended.
            // The other 9 resources are never appened, forcing the looping to keep waiting for the remaining resources.
            // This security exit clause will be activated after 100 seconds, cancelling the operation.

            const int numberOfResources = 10;
            const int maxWaitingTimeInSeconds = 120;

            BundleOrchestratorOperationType operationType = BundleOrchestratorOperationType.Batch;
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(maxWaitingTimeInSeconds));

            var dataStore = BundleTestsCommonFunctions.GetSubstituteForIFhirDataStore();
            var batchOrchestrator = BundleTestsCommonFunctions.GetBundleOrchestrator();

            IBundleOrchestratorOperation operation = batchOrchestrator.CreateNewOperation(operationType, "POST", numberOfResources);

            Assert.Equal(BundleOrchestratorOperationStatus.Open, operation.Status);
            Assert.Equal(operationType, operation.Type);

            List<Task> tasksWaitingForMergeAsync = new List<Task>(capacity: numberOfResources);
            DomainResource resource = BundleTestsCommonFunctions.GetSamplePatient(Guid.NewGuid());
            ResourceWrapperOperation resourceWrapper = await BundleTestsCommonFunctions.GetResourceWrapperOperationAsync(
                resource,
                new BundleResourceContext(Bundle.BundleType.Batch, BundleProcessingLogic.Parallel, GetHttpVerb(0), persistedId: null, operation.Id));

            Task<UpsertOutcome> appendedResourceTask = operation.AppendResourceAsync(resourceWrapper, dataStore, cts.Token);
            tasksWaitingForMergeAsync.Add(appendedResourceTask);

            try
            {
                await Task.WhenAll(tasksWaitingForMergeAsync.ToArray());
            }
            catch (OperationCanceledException)
            {
                Assert.Equal(BundleOrchestratorOperationStatus.Canceled, operation.Status);
                Assert.False(cts.IsCancellationRequested);

                return;
            }

            // Test should fail if the security exit clause is removed or if it does not raise a TaskCanceledException.
            Assert.Fail("There is a security exit clause in Bundle Orchestrator Operation. This clause was not activated: internal cancellation was supposed to happen before the external cancellation.");
        }

        [Theory]
        [InlineData(10, BundleOrchestratorOperationType.Batch)]
        [InlineData(100, BundleOrchestratorOperationType.Transaction)]
        [InlineData(500, BundleOrchestratorOperationType.Batch)]
        [InlineData(1000, BundleOrchestratorOperationType.Transaction)]
        public async Task GivenABatchOperation_WhenAllResourcedAreReleasedInParallel_ThenCancelTheOperation(int numberOfResources, BundleOrchestratorOperationType operationType)
        {
            // When all resources in a bundle are released, then the operation state changes to 'Canceled'.

            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var batchOrchestrator = BundleTestsCommonFunctions.GetBundleOrchestrator();

            IBundleOrchestratorOperation operation = batchOrchestrator.CreateNewOperation(operationType, "POST", numberOfResources);

            Assert.Equal(BundleOrchestratorOperationStatus.Open, operation.Status);
            Assert.Equal(operationType, operation.Type);

            // Append resources to an operation.
            List<Task> tasksWaitingForMergeAsync = new List<Task>(capacity: numberOfResources);
            Parallel.For(0, numberOfResources, (i, task) =>
            {
                Task releasedResourceTask = operation.ReleaseResourceAsync("Canceled due tests.", cts.Token);
                tasksWaitingForMergeAsync.Add(releasedResourceTask);
            });

            await Task.WhenAll(tasksWaitingForMergeAsync.ToArray());

            Assert.Equal(BundleOrchestratorOperationStatus.Canceled, operation.Status);
            Assert.Equal(numberOfResources, operation.OriginalExpectedNumberOfResources);
            Assert.Equal(0, operation.CurrentExpectedNumberOfResources);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(500)]
        public async Task GivenABatchOperation_WhenHalfOfResourcesAreReleasedInParallel_ThenBatchShouldProcessTheRemainingResources(int numberOfResources)
        {
            // This test validates the logic when half of resources in a bundle are released due an expected behavior, and the other half is expected to be processed.

            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var dataStore = BundleTestsCommonFunctions.GetSubstituteForIFhirDataStore();

            var batchOrchestrator = BundleTestsCommonFunctions.GetBundleOrchestrator();

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
                    ResourceWrapperOperation resourceWrapper = await BundleTestsCommonFunctions.GetResourceWrapperOperationAsync(
                        resource,
                        new BundleResourceContext(Bundle.BundleType.Batch, BundleProcessingLogic.Parallel, GetHttpVerb(i), persistedId: null, operation.Id));

                    appendTask = operation.AppendResourceAsync(resourceWrapper, dataStore, cts.Token);
                }

                tasksWaitingForMergeAsync.Add(appendTask);
            });

            await Task.WhenAll(tasksWaitingForMergeAsync.ToArray());

            Assert.Equal(BundleOrchestratorOperationStatus.Completed, operation.Status);
            Assert.Equal(numberOfResources, operation.OriginalExpectedNumberOfResources);
            Assert.Equal(numberOfResources / 2, operation.CurrentExpectedNumberOfResources);
        }

        [Fact]
        public async Task GivenABatchOperation_WhenAppendedOnlyOneOutOfAllSupposedResourcesIsAppended_ThenThrowATaskCanceledOperationDueTimeout()
        {
            // This test validated if the CancellationToken is respected and a Bundle Operation fails if waits for too long for all the resources.

            const int numberOfResources = 10;

            // Short cancellation time. This test should fail fast and throw a TaskCanceledException.
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var dataStore = Substitute.For<IFhirDataStore>();

            var batchOrchestrator = BundleTestsCommonFunctions.GetBundleOrchestrator();

            IBundleOrchestratorOperation operation = batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, "POST", expectedNumberOfResources: numberOfResources);

            Assert.Equal(BundleOrchestratorOperationStatus.Open, operation.Status);

            // Append resources to an operation.
            List<Task> tasksWaitingForMergeAsync = new List<Task>(capacity: numberOfResources);

            DomainResource resource = BundleTestsCommonFunctions.GetSamplePatient(Guid.NewGuid());
            ResourceWrapperOperation resourceWrapper = await BundleTestsCommonFunctions.GetResourceWrapperOperationAsync(
                resource,
                new BundleResourceContext(Bundle.BundleType.Batch, BundleProcessingLogic.Parallel, GetHttpVerb(index: 0), persistedId: null, operation.Id));

            // A single resource will be appended to this operation.
            // In this test, we are forcing the operation to timeout while waiting for the remain resources.
            tasksWaitingForMergeAsync.Add(operation.AppendResourceAsync(resourceWrapper, dataStore, cts.Token));

            AggregateException age = Assert.Throws<AggregateException>(() => Task.WaitAll(tasksWaitingForMergeAsync.ToArray()));

            Assert.True(age.InnerException is TaskCanceledException);
            Assert.Equal(BundleOrchestratorOperationStatus.Canceled, operation.Status);
            Assert.Equal(numberOfResources, operation.OriginalExpectedNumberOfResources);
            Assert.Equal(numberOfResources, operation.CurrentExpectedNumberOfResources);
        }

        [Fact]
        public async Task GivenABatchOperation_WhenAlreadyCanceled_ThenReleaseResourceAsyncShouldReturnWithoutThrowingBundleOrchestratorException()
        {
            // When a parallel bundle is canceled, subsequent ReleaseResourceAsync
            // calls from remaining workers must silently return rather than attempt an invalid
            // status transition that throws BundleOrchestratorException.

            const int numberOfResources = 10;

            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var batchOrchestrator = BundleTestsCommonFunctions.GetBundleOrchestrator();
            IBundleOrchestratorOperation operation = batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, "POST", numberOfResources);

            operation.Cancel("Simulated cancellation for test.");
            Assert.Equal(BundleOrchestratorOperationStatus.Canceled, operation.Status);

            Task[] releaseTasks = new Task[numberOfResources];
            for (int i = 0; i < numberOfResources; i++)
            {
                releaseTasks[i] = operation.ReleaseResourceAsync("Released after cancellation.", cts.Token);
            }

            Exception exception = await Record.ExceptionAsync(() => Task.WhenAll(releaseTasks));
            Assert.Null(exception);

            Assert.Equal(BundleOrchestratorOperationStatus.Canceled, operation.Status);
        }

        [Fact]
        public async Task GivenABatchOperation_WhenOneWorkerFailsAndRemainingWorkersRelease_ThenReleaseResourceAsyncShouldReturnWithoutThrowingBundleOrchestratorException()
        {
            // A bundle with N parallel workers is being processed. One worker fails (e.g., SQL timeout)
            // which sets the operation to Failed. The remaining N-1 workers then call ReleaseResourceAsync
            // concurrently and do not throw BundleOrchestratorException.

            const int numberOfResources = 10;
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var batchOrchestrator = BundleTestsCommonFunctions.GetBundleOrchestrator();
            IBundleOrchestratorOperation operation = batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, "POST", numberOfResources);

            // Worker 0 fails first (e.g., SQL timeout). AppendResourceAsync with a pre-cancelled
            // token triggers the internal catch block → status becomes Failed.
            using CancellationTokenSource preCancelled = new CancellationTokenSource();
            preCancelled.Cancel();

            DomainResource resource = BundleTestsCommonFunctions.GetSamplePatient(Guid.NewGuid());
            ResourceWrapperOperation resourceWrapper = await BundleTestsCommonFunctions.GetResourceWrapperOperationAsync(
                resource,
                new BundleResourceContext(Bundle.BundleType.Batch, BundleProcessingLogic.Parallel, Bundle.HTTPVerb.POST, persistedId: null, operation.Id));

            try
            {
                await operation.AppendResourceAsync(resourceWrapper, BundleTestsCommonFunctions.GetSubstituteForIFhirDataStore(), preCancelled.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected: the failing worker propagates the exception.
            }

            Assert.Equal(BundleOrchestratorOperationStatus.Failed, operation.Status);

            // Remaining N-1 workers call ReleaseResourceAsync concurrently after the failure. Operation is already Failed,
            // and these workers must not throw BundleOrchestratorException.
            Task[] releaseTasks = new Task[numberOfResources - 1];
            Parallel.For(1, numberOfResources, (i, _) =>
            {
                releaseTasks[i - 1] = operation.ReleaseResourceAsync($"Worker {i} releasing.", cts.Token);
            });

            Exception exception = await Record.ExceptionAsync(() => Task.WhenAll(releaseTasks));
            Assert.Null(exception);

            Assert.Equal(BundleOrchestratorOperationStatus.Failed, operation.Status);
        }

        private static Bundle.HTTPVerb GetHttpVerb(int index)
        {
            int nextHttpVerb = index % 6;
            return (Bundle.HTTPVerb)nextHttpVerb;
        }
    }
}
