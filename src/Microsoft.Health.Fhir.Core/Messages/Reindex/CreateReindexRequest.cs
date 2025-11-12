// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Medino;

namespace Microsoft.Health.Fhir.Core.Messages.Reindex
{
    public class CreateReindexRequest : IRequest<CreateReindexResponse>
    {
        public CreateReindexRequest(
            IReadOnlyCollection<string> targetResourceTypes,
            IReadOnlyCollection<string> targetSearchParameterTypes,
            uint? maximumResourcesPerQuery = null,
            uint? maximumResourcesPerWrite = null,
            int? queryDelayIntervalInMilliseconds = null,
            ushort? targetDataStoreUsagePercentage = null)
        {
            MaximumResourcesPerQuery = maximumResourcesPerQuery;
            MaximumResourcesPerWrite = maximumResourcesPerWrite;
            QueryDelayIntervalInMilliseconds = queryDelayIntervalInMilliseconds;
            TargetDataStoreUsagePercentage = targetDataStoreUsagePercentage;
            TargetResourceTypes = EnsureArg.IsNotNull(targetResourceTypes, nameof(targetResourceTypes));
            TargetSearchParameterTypes = EnsureArg.IsNotNull(targetSearchParameterTypes, nameof(targetSearchParameterTypes));
        }

        public IReadOnlyCollection<string> TargetResourceTypes { get; }

        public IReadOnlyCollection<string> TargetSearchParameterTypes { get; }

        public uint? MaximumResourcesPerQuery { get; }

        public uint? MaximumResourcesPerWrite { get; }

        public int? QueryDelayIntervalInMilliseconds { get; }

        public ushort? TargetDataStoreUsagePercentage { get; }
    }
}
