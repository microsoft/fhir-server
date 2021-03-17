// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    internal class QueryPartitionStatistics
    {
        private long _partitionSum;

        private long _executionCount;

        public void Update(int partitionCount)
        {
            lock (this)
            {
                _partitionSum += partitionCount;
                _executionCount++;
            }
        }

        public int? GetAveragePartitionCount()
        {
            lock (this)
            {
                return _executionCount == 0 ? null : (int)(_partitionSum / _executionCount);
            }
        }
    }
}
