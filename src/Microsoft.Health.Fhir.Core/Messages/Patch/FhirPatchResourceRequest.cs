// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Messages.Patch
{
    public sealed class FhirPatchResourceRequest : IRequest<UpsertResourceResponse>, IRequireCapability
    {
        public FhirPatchResourceRequest(ResourceKey resourceKey, Parameters paramResource, WeakETag weakETag = null)
        {
            EnsureArg.IsNotNull(resourceKey, nameof(resourceKey));
            EnsureArg.IsNotNull(paramResource, nameof(paramResource));

            ResourceKey = resourceKey;
            ParamResource = paramResource;
            WeakETag = weakETag;
        }

        public ResourceKey ResourceKey { get; }

        public Parameters ParamResource { get; }

        public WeakETag WeakETag { get; }

        public IEnumerable<CapabilityQuery> RequiredCapabilities()
        {
            yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{ResourceKey.ResourceType}').interaction.where(code = 'patch').exists()");
        }
    }
}
