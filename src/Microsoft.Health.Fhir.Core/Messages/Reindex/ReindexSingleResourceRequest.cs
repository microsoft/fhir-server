// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Reindex
{
    public class ReindexSingleResourceRequest : IRequest<ReindexSingleResourceResponse>
    {
        public ReindexSingleResourceRequest(string resourceType, string resourceId)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));
            EnsureArg.IsNotNullOrWhiteSpace(resourceId, nameof(resourceId));

            ResourceType = resourceType;
            ResourceId = resourceId;
        }

        public string ResourceType { get; }

        public string ResourceId { get; }
    }
}
