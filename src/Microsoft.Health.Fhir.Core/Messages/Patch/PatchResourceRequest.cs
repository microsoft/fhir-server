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
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Messages.Patch
{
    public sealed class PatchResourceRequest : IRequest<UpsertResourceResponse>, IRequireCapability
    {
        public PatchResourceRequest(ResourceKey resourceKey, JsonPatchDocument patchDocument, WeakETag weakETag = null)
        {
            EnsureArg.IsNotNull(resourceKey, nameof(resourceKey));
            EnsureArg.IsNotNull(patchDocument, nameof(patchDocument));

            ResourceKey = resourceKey;
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
