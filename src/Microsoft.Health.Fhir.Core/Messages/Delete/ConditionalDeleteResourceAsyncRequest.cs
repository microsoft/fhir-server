// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Messages.Delete;

public class ConditionalDeleteResourceAsyncRequest : ConditionalDeleteResourceRequestBase<ConditionalDeleteResourceAsyncResponse>
{
    public ConditionalDeleteResourceAsyncRequest(
        string resourceType,
        IReadOnlyList<Tuple<string, string>> conditionalParameters,
        DeleteOperation deleteOperation)
        : base(resourceType, conditionalParameters, deleteOperation)
    {
    }
}
