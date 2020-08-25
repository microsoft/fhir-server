// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Patch;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Core.Messages.Patch
{
    public class ConditionalPatchResourceRequest : IRequest<PatchResourceResponse>, IRequest, IRequireCapability
    {
        public ConditionalPatchResourceRequest(string resourceType, IPatchDocument patchDocument, IReadOnlyList<Tuple<string, string>> conditionalParameters)
        {
            EnsureArg.IsNotNull(resourceType, nameof(resourceType));
            EnsureArg.IsNotNull(patchDocument, nameof(patchDocument));
            EnsureArg.IsNotNull(conditionalParameters, nameof(conditionalParameters));

            ResourceType = resourceType;
            PatchDocument = patchDocument;
            ConditionalParameters = conditionalParameters;
        }

        public string ResourceType { get; }

        public IPatchDocument PatchDocument { get; }

        public IReadOnlyList<Tuple<string, string>> ConditionalParameters { get; }

        public IEnumerable<CapabilityQuery> RequiredCapabilities()
        {
            yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{ResourceType}').interaction.where(code = 'patch').exists()");
            yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{ResourceType}').conditionalUpdate = true");
        }
    }
}
