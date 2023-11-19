// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Microsoft.Health.Fhir.Core.Logging;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Logging
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class LongRunningOperationStatisticsTests
    {
        [Fact]
        public void GivenAnLongRunningOperation_WhenUsingLooging_ThenCreateStatistics()
        {
            const int count = 10;

            LongRunningOperationStatistics statistics = new LongRunningOperationStatistics("operation");

            statistics.StartCollectingResults();

            for (int i = 0; i < count; i++)
            {
                statistics.Iterate();
                Thread.Sleep(10);
            }

            statistics.StopCollectingResults();
            string json = statistics.GetStatisticsAsJson();

            Assert.Equal(count, statistics.IterationCount);
            Assert.Contains(statistics.GetLoggingCategory(), json);
        }
    }
}
