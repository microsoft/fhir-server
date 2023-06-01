// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Create
{
    public sealed class ConditionalCreateResourceRequest : ConditionalResourceRequest<UpsertResourceResponse>
    {
        private static readonly string[] Capabilities = new string[1] { "conditionalCreate = true" };

        public ConditionalCreateResourceRequest(ResourceElement resource, IReadOnlyList<Tuple<string, string>> conditionalParameters, Guid? bundleOperationId = null)
            : base(resource.InstanceType, conditionalParameters, bundleOperationId)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            Resource = resource;
        }

        public ResourceElement Resource { get; }

        protected override IEnumerable<string> GetCapabilities() => Capabilities;
    }
}
