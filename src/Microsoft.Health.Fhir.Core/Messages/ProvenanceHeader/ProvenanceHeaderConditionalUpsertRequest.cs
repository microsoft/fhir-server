// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.ProvenanceHeader
{
    public class ProvenanceHeaderConditionalUpsertRequest : IRequest<UpsertResourceResponse>, IRequest
    {
        public ProvenanceHeaderConditionalUpsertRequest(ResourceElement target, IReadOnlyList<Tuple<string, string>> conditionalParameters, string provenanceHeader)
        {
            EnsureArg.IsNotNull(target, nameof(target));
            EnsureArg.IsNotNull(conditionalParameters, nameof(conditionalParameters));
            EnsureArg.IsNotNullOrWhiteSpace(provenanceHeader, nameof(provenanceHeader));

            Target = target;
            ConditionalParameters = conditionalParameters;
            ProvenanceHeader = provenanceHeader;
        }

        public IReadOnlyList<Tuple<string, string>> ConditionalParameters { get; }

        public ResourceElement Target { get; }

        public string ProvenanceHeader { get; }
    }
}
