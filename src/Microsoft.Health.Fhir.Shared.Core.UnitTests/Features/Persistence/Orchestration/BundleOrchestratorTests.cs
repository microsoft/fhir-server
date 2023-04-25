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
    [Trait(Traits.Category, Categories.Bundle)]
    [Trait(Traits.Category, Categories.BundleOrchestrator)]
    public class BundleOrchestratorTests
    {
        [Theory]
        [InlineData(BundleOrchestratorOperationType.Batch)]
        [InlineData(BundleOrchestratorOperationType.Transaction)]
        public void GivenAnOrchestrator_WhenAskedForAJob_ReceiveANewJobBack(BundleOrchestratorOperationType operationType)
        {
            const string label = "label";
            const int expectedNumberOfResources = 100;

            var batchOrchestrator = new BundleOrchestrator(isEnabled: true);

            IBundleOrchestratorOperation operation = batchOrchestrator.CreateNewOperation(operationType, label, expectedNumberOfResources);

            Assert.Equal(label, operation.Label);
            Assert.Equal(expectedNumberOfResources, operation.OriginalExpectedNumberOfResources);
            Assert.Equal(operationType, operation.Type);

            batchOrchestrator.CompleteOperation(operation);
        }

        [Fact]
        public void GivenAnOrchestrator_WhenAskedForAJobWithInvalidParameters_ReceiveArgumentExpections()
        {
            var batchOrchestrator = new BundleOrchestrator(isEnabled: true);

            // Fail: Providing invalid labels.
            Assert.Throws<ArgumentNullException>(() => batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, null, expectedNumberOfResources: 100));
            Assert.Throws<ArgumentException>(() => batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, string.Empty, expectedNumberOfResources: 100));

            // Fail: Providing invalid number of resources.
            Assert.Throws<ArgumentOutOfRangeException>(() => batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, "test", expectedNumberOfResources: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => batchOrchestrator.CreateNewOperation(BundleOrchestratorOperationType.Batch, "test", expectedNumberOfResources: 0));

            // Fail: Trying to complete an operation that does not exist in the BundleOrchestrator.
            Assert.Throws<BundleOrchestratorException>(() => batchOrchestrator.CompleteOperation(new BundleOrchestratorOperation(BundleOrchestratorOperationType.Batch, "x", 100)));
        }
    }
}
