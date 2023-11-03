// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Get
{
    public class GetResourceRequest : IRequest<GetResourceResponse>, IRequireCapability
    {
        public GetResourceRequest(ResourceKey resourceKey, BundleResourceContext bundleResourceContext = null)
        {
            EnsureArg.IsNotNull(resourceKey, nameof(resourceKey));

            ResourceKey = resourceKey;
            BundleResourceContext = bundleResourceContext;
        }

        public GetResourceRequest(string type, string id, BundleResourceContext bundleResourceContext = null)
        {
            EnsureArg.IsNotNull(type, nameof(type));
            EnsureArg.IsNotNull(id, nameof(id));

            ResourceKey = new ResourceKey(type, id);
            BundleResourceContext = bundleResourceContext;
        }

        public GetResourceRequest(string type, string id, string versionId, BundleResourceContext bundleResourceContext)
        {
            EnsureArg.IsNotNull(type, nameof(type));
            EnsureArg.IsNotNull(id, nameof(id));
            EnsureArg.IsNotNull(versionId, nameof(versionId));

            ResourceKey = new ResourceKey(type, id, versionId);
            BundleResourceContext = bundleResourceContext;
        }

        public ResourceKey ResourceKey { get; }

        public BundleResourceContext BundleResourceContext { get; }

        public IEnumerable<CapabilityQuery> RequiredCapabilities()
        {
            if (string.IsNullOrWhiteSpace(ResourceKey.VersionId))
            {
                yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{ResourceKey.ResourceType}').interaction.where(code = 'read').exists()");
            }
            else
            {
                yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{ResourceKey.ResourceType}').interaction.where(code = 'vread').exists()");
            }
        }
    }
}
