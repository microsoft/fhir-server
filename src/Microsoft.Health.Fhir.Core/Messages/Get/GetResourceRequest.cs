﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Messages.Get
{
    public class GetResourceRequest : IRequest<GetResourceResponse>, IRequireCapability
    {
        public GetResourceRequest(ResourceKey resourceKey, Guid? bundleOperationId = null)
        {
            EnsureArg.IsNotNull(resourceKey, nameof(resourceKey));

            ResourceKey = resourceKey;
            BundleOperationId = bundleOperationId;
        }

        public GetResourceRequest(string type, string id, Guid? bundleOperationId = null)
        {
            EnsureArg.IsNotNull(type, nameof(type));
            EnsureArg.IsNotNull(id, nameof(id));

            ResourceKey = new ResourceKey(type, id);
            BundleOperationId = bundleOperationId;
        }

        public GetResourceRequest(string type, string id, string versionId, Guid? bundleOperationId)
        {
            EnsureArg.IsNotNull(type, nameof(type));
            EnsureArg.IsNotNull(id, nameof(id));
            EnsureArg.IsNotNull(versionId, nameof(versionId));

            ResourceKey = new ResourceKey(type, id, versionId);
            BundleOperationId = bundleOperationId;
        }

        public ResourceKey ResourceKey { get; }

        public Guid? BundleOperationId { get; }

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
