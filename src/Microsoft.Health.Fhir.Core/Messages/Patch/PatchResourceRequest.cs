// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Messages.Patch
{
    public class PatchResourceRequest : IRequest<PatchResourceResponse>, IRequireCapability
    {
        public PatchResourceRequest(ResourceKey resourceKey, JsonPatchDocument patchDocument)
        {
            EnsureArg.IsNotNull(resourceKey, nameof(resourceKey));

            ResourceKey = resourceKey;
            PatchDocument = patchDocument;
        }

        public PatchResourceRequest(string type, string id, JsonPatchDocument patchDocument, WeakETag weakETag = null)
        {
            EnsureArg.IsNotNull(type, nameof(type));
            EnsureArg.IsNotNull(id, nameof(id));

            ResourceKey = new ResourceKey(type, id);
            PatchDocument = patchDocument;
            WeakETag = weakETag;
        }

        public ResourceKey ResourceKey { get; }

        public JsonPatchDocument PatchDocument { get; }

        public WeakETag WeakETag { get; }

        public IEnumerable<CapabilityQuery> RequiredCapabilities()
        {
            yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{ResourceKey.ResourceType}').interaction.where(code = 'patch').exists()");
        }
    }
}
