// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
            BatchOrchestratorJob<object> job = batchOrchestrator.CreateNewJob(label, expectedNumberOfResources);

            Assert.Equal(label, job.Label);
            Assert.Equal(expectedNumberOfResources, job.OriginalExpectedNumberOfResources);
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

        [Fact]
        public void X()
        {
            const int numberOfResources = 10;

            var batchOrchestrator = new BatchOrchestrator<object>();
            BatchOrchestratorJob<object> job = batchOrchestrator.CreateNewJob("INSERT", numberOfResources);

            Assert.Equal(BatchOrchestratorJobStatus.Open, job.Status);

            // Append resources to a job.
            for (int i = 0; i < numberOfResources; i++)
            {
                object newResource = new object();
                job.AppendResource(newResource);

                if (i == (numberOfResources - 1))
                {
                    Assert.Equal(BatchOrchestratorJobStatus.Processing, job.Status);
                }
                else
                {
                    Assert.Equal(BatchOrchestratorJobStatus.Waiting, job.Status);
                }
            }
        }
    }
}
