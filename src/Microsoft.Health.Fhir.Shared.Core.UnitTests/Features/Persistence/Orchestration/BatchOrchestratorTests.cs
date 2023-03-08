// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Persistence.Orchestration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Batch)]
    public class BatchOrchestratorTests
    {
        [Fact]
        public void GivenAnOrchestrator_WhenAskedForAJob_ReceiveANewJobBack()
        {
            const string label = "label";
            const int expectedNumberOfResources = 100;

            var batchOrchestrator = new BatchOrchestrator<object>();
            BatchOrchestratorOperation<object> operation = batchOrchestrator.CreateNewJob(label, expectedNumberOfResources);

            Assert.Equal(label, operation.Label);
            Assert.Equal(expectedNumberOfResources, operation.OriginalExpectedNumberOfResources);
        }

        [Fact]
        public void GivenAnOrchestrator_WhenAskedForAJobWithInvalidParameters_ReceiveArgumentExpections()
        {
            var batchOrchestrator = new BatchOrchestrator<object>();

            Assert.Throws<ArgumentNullException>(() => batchOrchestrator.CreateNewJob(null, expectedNumberOfResources: 100));

            Assert.Throws<ArgumentException>(() => batchOrchestrator.CreateNewJob(string.Empty, expectedNumberOfResources: 100));

            Assert.Throws<ArgumentOutOfRangeException>(() => batchOrchestrator.CreateNewJob("test", expectedNumberOfResources: -1));

            Assert.Throws<ArgumentOutOfRangeException>(() => batchOrchestrator.CreateNewJob("test", expectedNumberOfResources: 0));
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(500)]
        public void GivenAnOrchestrator_WhenAppendedMultipleResourcesInSequenceWaitForAllToBeAppended_ThenCompleteWithSuccess(int numberOfResources)
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var batchOrchestrator = new BatchOrchestrator<object>();
            BatchOrchestratorOperation<object> operation = batchOrchestrator.CreateNewJob("PUT", numberOfResources);

            Assert.Equal(BatchOrchestratorOperationStatus.Open, operation.Status);

            // Append resources to a job.
            Task[] tasksWaitingForMergeAsync = new Task[numberOfResources];
            for (int i = 0; i < numberOfResources; i++)
            {
                object newResource = new object();

                Task appendedResourceTask = operation.AppendResourceAsync(newResource, cts.Token);
                tasksWaitingForMergeAsync[i] = appendedResourceTask;

                if (i == (numberOfResources - 1))
                {
                    Task.WaitAll(tasksWaitingForMergeAsync);

                    // After waiting for all tasks, the operation should be completed.
                    Assert.Equal(BatchOrchestratorOperationStatus.Completed, operation.Status);
                }
                else
                {
                    Assert.Equal(BatchOrchestratorOperationStatus.WaitingForResources, operation.Status);
                }
            }
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(500)]
        public void GivenAnOrchestrator_WhenAppendedMultipleResourcesInParallelWaitForAllToBeAppended_ThenCompleteWithSuccess(int numberOfResources)
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var batchOrchestrator = new BatchOrchestrator<object>();
            BatchOrchestratorOperation<object> operation = batchOrchestrator.CreateNewJob("POST", numberOfResources);

            Assert.Equal(BatchOrchestratorOperationStatus.Open, operation.Status);

            // Append resources to a job.
            List<Task> tasksWaitingForMergeAsync = new List<Task>(capacity: numberOfResources);
            Parallel.For(0, numberOfResources, (i, task) =>
            {
                object newResource = i;
                Task appendedResourceTask = operation.AppendResourceAsync(newResource, cts.Token);

                tasksWaitingForMergeAsync.Add(appendedResourceTask);
            });

            Task.WaitAll(tasksWaitingForMergeAsync.ToArray());
            Assert.Equal(BatchOrchestratorOperationStatus.Completed, operation.Status);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(500)]
        public void GivenAnOrchestrator_WhenAllResourcedAreReleasedInParallel_ThenCancelTheOperation(int numberOfResources)
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(300));

            var batchOrchestrator = new BatchOrchestrator<object>();
            BatchOrchestratorOperation<object> operation = batchOrchestrator.CreateNewJob("POST", numberOfResources);

            Assert.Equal(BatchOrchestratorOperationStatus.Open, operation.Status);

            // Append resources to a job.
            List<Task> tasksWaitingForMergeAsync = new List<Task>(capacity: numberOfResources);
            Parallel.For(0, numberOfResources, (i, task) =>
            {
                object newResource = i;
                Task releasedResourceTask = operation.ReleaseResourceAsync("Canceled due tests.", cts.Token);

                tasksWaitingForMergeAsync.Add(releasedResourceTask);
            });

            Task.WaitAll(tasksWaitingForMergeAsync.ToArray());
            Assert.Equal(BatchOrchestratorOperationStatus.Canceled, operation.Status);
        }
    }
}
