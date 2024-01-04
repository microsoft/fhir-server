// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public interface IThrottleableJobRecord
    {
        public uint MaximumNumberOfResourcesPerQuery { get; set; }

        /// <summary>
        /// Controls the time between queries of resources to be reindexed
        /// </summary>
        public int QueryDelayIntervalInMilliseconds { get; set; }

        /// <summary>
        /// Controls the target percentage of how much of the allocated
        /// data store resources to use
        /// Ex: 1 - 100 percent of provisioned datastore resources
        /// 0 means the value is not set, no throttling will occur
        /// </summary>
        public ushort? TargetDataStoreUsagePercentage { get; set; }
    }
}
