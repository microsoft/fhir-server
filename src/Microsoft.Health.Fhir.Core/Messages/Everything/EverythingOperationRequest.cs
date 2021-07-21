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
        public EverythingOperationRequest(string everythingOperationType, string resourceId, PartialDateTime start = null, PartialDateTime end = null, PartialDateTime since = null, string resourceTypes = null, string continuationToken = null)
        {
            EnsureArg.IsNotNullOrWhiteSpace(everythingOperationType, nameof(everythingOperationType));
            EnsureArg.IsNotNullOrWhiteSpace(resourceId, nameof(resourceId));

            EverythingOperationType = everythingOperationType;
            ResourceId = resourceId;
            Start = start;
            End = end;
            Since = since;
            ResourceTypes = resourceTypes;
            ContinuationToken = continuationToken;
        }

        public string EverythingOperationType { get; }

        public string ResourceId { get; }

        public PartialDateTime Start { get; }

        public PartialDateTime End { get; }

        public PartialDateTime Since { get; }

        public string ResourceTypes { get; }

        public string ContinuationToken { get; }
    }
}
