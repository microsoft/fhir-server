// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Messages.Create
{
    public class CreateResourceRequest : IRequest<UpsertResourceResponse>, IRequest, IRequireCapability
    {
        public CreateResourceRequest(Resource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            Resource = resource;
        }

        public Resource Resource { get; }

        public IEnumerable<CapabilityQuery> RequiredCapabilities()
        {
            yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{Resource.TypeName}').interaction.where(code = 'create').exists()");
        }
    }
}
