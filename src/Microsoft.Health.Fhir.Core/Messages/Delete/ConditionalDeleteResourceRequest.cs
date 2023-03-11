// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Messages.Delete
{
    public sealed class ConditionalDeleteResourceRequest : ConditionalDeleteResourceRequestBase<DeleteResourceResponse>
    {
        public ConditionalDeleteResourceRequest(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> conditionalParameters,
            DeleteOperation deleteOperation,
            int maxDeleteCount,
            Guid? bundleOperationId = null)
            : base(resourceType, conditionalParameters, deleteOperation, bundleOperationId)
        {
            MaxDeleteCount = maxDeleteCount;
        }

        public int MaxDeleteCount { get; }
    }
}
