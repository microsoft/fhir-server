// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class TransactionWatchdogTests
    {
        [Fact]
        public void GivenTimedOutTransactionContext_WhenFormattingWatchdogEvent_ThenRequestCorrelationIsIncluded()
        {
            var transaction = new SqlStoreClient.MergeResourcesTransaction(
                123,
                "correlationId=correlation-123;method=POST;routeName=BatchOrTransaction");

            string message = TransactionWatchdog.FormatTransactionEventText("committed", transaction, resources: 2);

            Assert.Contains("committed transaction=123", message, System.StringComparison.Ordinal);
            Assert.Contains("resources=2", message, System.StringComparison.Ordinal);
            Assert.Contains("correlationId=correlation-123", message, System.StringComparison.Ordinal);
            Assert.Contains("routeName=BatchOrTransaction", message, System.StringComparison.Ordinal);
        }

        [Fact]
        public void GivenTimedOutTransactionWithoutContext_WhenFormattingWatchdogEvent_ThenMessageStillContainsTransaction()
        {
            var transaction = new SqlStoreClient.MergeResourcesTransaction(123, null);

            string message = TransactionWatchdog.FormatTransactionEventText("found timed out", transaction);

            Assert.Equal("found timed out transaction=123", message);
        }
    }
}
