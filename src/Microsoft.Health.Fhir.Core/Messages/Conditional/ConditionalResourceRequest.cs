// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages
{
    public abstract class ConditionalResourceRequest<TResponse> : IRequireCapability, IRequest<TResponse>
    {
        protected ConditionalResourceRequest(string resourceType, IReadOnlyList<Tuple<string, string>> conditionalParameters, BundleResourceContext bundleResourceContext)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));
            EnsureArg.IsNotNull(conditionalParameters, nameof(conditionalParameters));

            ResourceType = resourceType;
            ConditionalParameters = conditionalParameters;
            BundleResourceContext = bundleResourceContext;
        }

        public string ResourceType { get; }

        public IReadOnlyList<Tuple<string, string>> ConditionalParameters { get; }

        public BundleResourceContext BundleResourceContext { get; }

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
