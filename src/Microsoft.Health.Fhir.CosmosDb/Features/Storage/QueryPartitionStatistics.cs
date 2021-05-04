// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Used to keep track of the average number of partitions that the SDK reads from when executing a query.
    /// </summary>
    internal class QueryPartitionStatistics
    {
        private long _partitionSum;

        private long _executionCount;

        public void Update(int partitionCount)
        {
#pragma warning disable CA2002
            lock (this) // lgtm[cs/lock-this] no need to allocate another lock object for this internal class
            {
                _partitionSum += partitionCount;
                _executionCount++;
            }
        }

        public int? GetAveragePartitionCount()
        {
            lock (this) // lgtm[cs/lock-this] no need to allocate another lock object for this internal class
            {
                return _executionCount == 0 ? null : (int)(_partitionSum / _executionCount);
            }
#pragma warning restore CA2002
        }
    }
}
