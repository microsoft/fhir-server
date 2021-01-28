// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.ProvenanceHeader
{
    public class ProvenanceHeaderUpdateRequest : IRequest<UpsertResourceResponse>, IRequest
    {
        public ProvenanceHeaderUpdateRequest(ResourceElement target, string provenanceHeader, WeakETag weakETag = null)
        {
            EnsureArg.IsNotNull(target, nameof(target));
            EnsureArg.IsNotNullOrWhiteSpace(provenanceHeader, nameof(provenanceHeader));

            Target = target;
            WeakETag = weakETag;
            ProvenanceHeader = provenanceHeader;
        }

        public ResourceElement Target { get; }

        public WeakETag WeakETag { get; }

        public string ProvenanceHeader { get; }
    }
}
