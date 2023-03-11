// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Messages.Delete;

public class GetConditionalDeleteResourceAsyncResponse
{
    public GetConditionalDeleteResourceAsyncResponse(Resource jobResult, HttpStatusCode jobStatus, bool isCompleted)
    {
        JobResult = jobResult;
        JobStatus = jobStatus;
        IsCompleted = isCompleted;
    }

    public Resource JobResult { get; set; }

    public HttpStatusCode JobStatus { get; }

    public bool IsCompleted { get; }
}
