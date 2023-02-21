// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Queues;

public class JobGroupWrapper : SystemData
{
    public const string JobInfoPartitionKey = "__jobs__";

    [JsonProperty("groupId")]
    public long GroupId { get; set; }

    [JsonProperty(KnownDocumentProperties.PartitionKey)]
    public string PartitionKey { get; } = JobInfoPartitionKey;

    [JsonProperty("queueType")]
    public byte QueueType { get; set; }

    [JsonProperty("priority")]
    public long Priority { get; set; }

    [JsonProperty("definitions")]
    public IList<JobDefinitionWrapper> Definitions { get; } = new List<JobDefinitionWrapper>();

    [JsonProperty("createDate")]
    public DateTimeOffset CreateDate { get; set; }

    [JsonProperty("ttl")]
    public long TimeToLive { get; set; }
}
