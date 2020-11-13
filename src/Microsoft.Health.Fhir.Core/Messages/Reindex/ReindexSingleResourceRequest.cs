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
        public ReindexSingleResourceRequest(string httpMethod, string resourceType, string resourceId)
        {
            EnsureArg.IsNotNullOrWhiteSpace(httpMethod, nameof(httpMethod));
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));
            EnsureArg.IsNotNullOrWhiteSpace(resourceId, nameof(resourceId));

            HttpMethod = httpMethod;
            ResourceType = resourceType;
            ResourceId = resourceId;
        }

        public string HttpMethod { get; }

        public string ResourceType { get; }

        public string ResourceId { get; }
    }
}
