// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Messages.Delete
{
    public class DeleteResourceRequest : IRequest<DeleteResourceResponse>, IRequireCapability
    {
        public DeleteResourceRequest(ResourceKey resourceKey, DeleteOperation deleteOperation)
        {
            EnsureArg.IsNotNull(resourceKey, nameof(resourceKey));

            ResourceKey = resourceKey;
            DeleteOperation = deleteOperation;
        }

        public DeleteResourceRequest(string type, string id, DeleteOperation deleteOperation)
        {
            EnsureArg.IsNotNull(type, nameof(type));
            EnsureArg.IsNotNull(id, nameof(id));

            ResourceKey = new ResourceKey(type, id);
            DeleteOperation = deleteOperation;
        }

        public ResourceKey ResourceKey { get; }

        public DeleteOperation DeleteOperation { get; }

        public IEnumerable<CapabilityQuery> RequiredCapabilities()
        {
            yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{ResourceKey.ResourceType}').interaction.where(code = 'delete').exists()");
        }
    }
}
