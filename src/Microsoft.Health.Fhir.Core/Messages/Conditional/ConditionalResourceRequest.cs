// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Core.Messages
{
    public abstract class ConditionalResourceRequest<TResponse> : IRequireCapability, IRequest<TResponse>
    {
        protected ConditionalResourceRequest(string resourceType, IReadOnlyList<Tuple<string, string>> conditionalParameters, Guid? bundleOperationId)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));
            EnsureArg.IsNotNull(conditionalParameters, nameof(conditionalParameters));

            ResourceType = resourceType;
            ConditionalParameters = conditionalParameters;
            BundleOperationId = bundleOperationId;
        }

        public string ResourceType { get; }

        public IReadOnlyList<Tuple<string, string>> ConditionalParameters { get; }

        public Guid? BundleOperationId { get; }

        protected abstract IEnumerable<string> GetCapabilities();

        public IEnumerable<CapabilityQuery> RequiredCapabilities()
        {
            foreach (var capability in GetCapabilities())
            {
                yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{ResourceType}').{capability}");
            }
        }
    }
}
