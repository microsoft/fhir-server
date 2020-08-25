// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Patch;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Messages.Patch
{
    public class PatchResourceRequest : IRequest<PatchResourceResponse>, IRequest, IRequireCapability
    {
        public PatchResourceRequest(ResourceKey key, IPatchDocument patchDocument, WeakETag weakETag = null)
        {
            EnsureArg.IsNotNull(key, nameof(key));
            EnsureArg.IsNotNull(patchDocument, nameof(patchDocument));

            ResourceKey = key;
            PatchDocument = patchDocument;
            WeakETag = weakETag;
        }

        public ResourceKey ResourceKey { get; }

        public IPatchDocument PatchDocument { get; }

        public WeakETag WeakETag { get; }

        public IEnumerable<CapabilityQuery> RequiredCapabilities()
        {
            yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{ResourceKey.ResourceType}').interaction.where(code = 'patch').exists()");
        }
    }
}
