// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Upsert
{
    public class UpsertResourceRequest : IRequest<UpsertResourceResponse>, IRequest, IRequireCapability
    {
        public UpsertResourceRequest(ResourceElement resource, Guid? bundleOperationId = null, WeakETag weakETag = null)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            Resource = resource;
            BundleOperationId = bundleOperationId;
            WeakETag = weakETag;
        }

        public ResourceElement Resource { get; }

        public Guid? BundleOperationId { get; }

        public WeakETag WeakETag { get; }

        public IEnumerable<CapabilityQuery> RequiredCapabilities()
        {
            yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{Resource.InstanceType}').interaction.where(code = 'update').exists()");
        }
    }
}
