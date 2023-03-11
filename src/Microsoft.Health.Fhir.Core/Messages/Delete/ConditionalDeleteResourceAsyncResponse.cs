// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Messages.Delete;

public class ConditionalDeleteResourceAsyncResponse
{
    public ConditionalDeleteResourceAsyncResponse(string jobId)
    {
        EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

        JobId = jobId;
    }

    public string JobId { get; }
}
