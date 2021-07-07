// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Core.Messages.Delete
{
    public class ConditionalDeleteResourceRequest : IRequest<DeleteResourceResponse>, IRequireCapability
    {
        public ConditionalDeleteResourceRequest(string resourceType, IReadOnlyList<Tuple<string, string>> conditionalParameters, DeleteOperation deleteOperation, int maxDeleteCount)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));
            EnsureArg.IsNotNull(conditionalParameters, nameof(conditionalParameters));

            ResourceType = resourceType;
            ConditionalParameters = conditionalParameters;
            DeleteOperation = deleteOperation;
            MaxDeleteCount = maxDeleteCount;
        }

        public string ResourceType { get; }

        public IReadOnlyList<Tuple<string, string>> ConditionalParameters { get; }

        public DeleteOperation DeleteOperation { get; }

        public int MaxDeleteCount { get; }

        public IEnumerable<CapabilityQuery> RequiredCapabilities()
        {
            yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{ResourceType}').conditionalDelete.exists()");
            yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{ResourceType}').conditionalDelete != 'not-supported'");
        }
    }
}
