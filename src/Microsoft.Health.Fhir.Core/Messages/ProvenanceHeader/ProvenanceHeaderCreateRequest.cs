// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.ProvenanceHeader
{
    public class ProvenanceHeaderCreateRequest : IRequest<RawResourceElement>, IRequest
    {
        public ProvenanceHeaderCreateRequest(ResourceElement target, string provenanceHeader)
        {
            EnsureArg.IsNotNull(target, nameof(target));
            EnsureArg.IsNotNullOrWhiteSpace(provenanceHeader, nameof(provenanceHeader));

            Target = target;
            ProvenanceHeader = provenanceHeader;
        }

        public ResourceElement Target { get; }

        public string ProvenanceHeader { get; }
    }
}
