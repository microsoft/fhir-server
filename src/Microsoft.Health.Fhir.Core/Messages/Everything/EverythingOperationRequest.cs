// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Everything
{
    public class EverythingOperationRequest : IRequest<EverythingOperationResponse>
    {
        public EverythingOperationRequest(string resourceType, string resourceId, PartialDateTime start = null, PartialDateTime end = null, PartialDateTime since = null, string type = null, int? count = null, string continuationToken = null)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));
            EnsureArg.IsNotNullOrWhiteSpace(resourceId, nameof(resourceId));

            ResourceType = resourceType;
            ResourceId = resourceId;
            Start = start;
            End = end;
            Since = since;
            Type = type;
            Count = count;
            ContinuationToken = continuationToken;
        }

        public string ResourceType { get; }

        public string ResourceId { get; }

        public PartialDateTime Start { get; }

        public PartialDateTime End { get; }

        public PartialDateTime Since { get; }

        public string Type { get; }

        public int? Count { get; }

        public string ContinuationToken { get; }
    }
}
