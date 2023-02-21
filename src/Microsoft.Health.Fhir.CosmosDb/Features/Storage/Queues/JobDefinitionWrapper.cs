// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Queues;

public class JobDefinitionWrapper
{
    [JsonProperty("jobId")]
    public long JobId { get; set; }

    [JsonProperty("status")]
    public byte? Status { get; set; }

    [JsonProperty("worker")]
    public string Worker { get; set; }

    [JsonProperty("definition")]
    public string Definition { get; set; }

    [JsonProperty("definitionHash")]
    public string DefinitionHash { get; set; }

    [JsonProperty("result")]
    public string Result { get; set; }

    [JsonProperty("data")]
    public long? Data { get; set; }

    [JsonProperty("startDate")]
    public DateTimeOffset? StartDate { get; set; }

    [JsonProperty("endDate")]
    public DateTimeOffset? EndDate { get; set; }

    [JsonProperty("heartbeatDateTime")]
    public DateTimeOffset HeartbeatDateTime { get; set; }

    [JsonProperty("cancelRequested")]
    public bool CancelRequested { get; set; }

    [JsonProperty("info")]
    public string Info { get; set; }

    [JsonProperty("dequeueCount")]
    public int DequeueCount { get; set; }

    [JsonProperty("version")]
    public long Version { get; set; }
}
