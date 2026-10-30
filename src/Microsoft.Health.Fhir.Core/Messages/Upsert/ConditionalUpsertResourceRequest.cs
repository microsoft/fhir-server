// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Upsert
{
    public sealed class ConditionalUpsertResourceRequest : ConditionalResourceRequest<UpsertResourceResponse>
    {
        private static readonly string[] Capabilities = new string[1] { "conditionalUpdate = true" };

        public ConditionalUpsertResourceRequest(
            ResourceElement resource,
            IReadOnlyList<Tuple<string, string>> conditionalParameters,
            BundleResourceContext bundleResourceContext = null,
            ConditionalUpdateNoMatchBehavior noMatchBehavior = ConditionalUpdateNoMatchBehavior.Upsert)
            : base(resource.InstanceType, conditionalParameters, bundleResourceContext)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.EnumIsDefined(noMatchBehavior, nameof(noMatchBehavior));
            Resource = resource;
            NoMatchBehavior = noMatchBehavior;
        }

        public ResourceElement Resource { get; }

        /// <summary>
        /// How the resource is saved when the search finds no match and the resource has a client-supplied id.
        /// </summary>
        public ConditionalUpdateNoMatchBehavior NoMatchBehavior { get; }

        protected override IEnumerable<string> GetCapabilities() => Capabilities;
    }
}
