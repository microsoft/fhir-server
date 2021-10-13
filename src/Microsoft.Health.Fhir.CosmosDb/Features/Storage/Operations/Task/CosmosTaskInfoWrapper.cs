// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations.Task
{
    /// <summary>
    /// A wrapper around the <see cref="TaskInfo"/> class that contains metadata specific to CosmosDb.
    /// </summary>
    internal class CosmosTaskInfoWrapper : CosmosJobRecordWrapper
    {
        public CosmosTaskInfoWrapper(TaskInfo taskInfo)
        {
            EnsureArg.IsNotNull(taskInfo, nameof(taskInfo));

            TaskInfo = taskInfo;
        }

        [JsonConstructor]
        protected CosmosTaskInfoWrapper()
        {
        }

        [JsonProperty(KnownDocumentProperties.PartitionKey)]
        public override string PartitionKey { get; } = CosmosDbTaskConstants.TaskPartitionKey;

        [JsonProperty(JobRecordProperties.JobRecord)]
        public TaskInfo TaskInfo { get; private set; }
    }
}
