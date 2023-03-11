// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Delete;

[SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO class")]
public class ConditionalDeleteJobInfo : IJobData
{
    public ConditionalDeleteJobInfo(
        int typeId,
        string resourceType,
        ICollection<Tuple<string, string>> conditionalParameters,
        DeleteOperation deleteOperation,
        string principal,
        string activityId,
        Uri requestUri,
        Uri baseUri)
    {
        TypeId = EnsureArg.IsInRange(typeId, (int)JobType.ConditionalDeleteProcessing, (int)JobType.ConditionalDeleteOrchestrator);
        ResourceType = EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));
        ConditionalParameters = EnsureArg.IsNotNull(conditionalParameters, nameof(conditionalParameters));
        DeleteOperation = deleteOperation;
        ActivityId = EnsureArg.IsNotNullOrEmpty(activityId, nameof(activityId));
        Principal = EnsureArg.IsNotNullOrEmpty(principal, nameof(principal));
        RequestUri = EnsureArg.IsNotNull(requestUri, nameof(requestUri));
        BaseUri = EnsureArg.IsNotNull(baseUri, nameof(baseUri));
    }

    [JsonConstructor]
    protected ConditionalDeleteJobInfo()
    {
    }

    [JsonProperty("typeId")]
    public int TypeId { get; protected set; }

    [JsonProperty("resourceType")]
    public string ResourceType { get; protected set; }

    [JsonProperty("conditionalParameters")]
    public ICollection<Tuple<string, string>> ConditionalParameters { get; protected set; }

    [JsonProperty("deleteOperation")]
    public DeleteOperation DeleteOperation { get; protected set; }

    [JsonProperty("activityId")]
    public string ActivityId { get; protected set; }

    [JsonProperty("principal")]
    public string Principal { get; protected set; }

    /// <summary>
    /// Request Uri for the import operation
    /// </summary>
    [JsonProperty("requestUri")]
    public Uri RequestUri { get; set; }

    /// <summary>
    /// FHIR Base Uri
    /// </summary>
    [JsonProperty("baseUri")]
    public Uri BaseUri { get; set; }
}
