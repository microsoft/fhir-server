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
            bool createOnlyOnNoMatch = false)
            : base(resource.InstanceType, conditionalParameters, bundleResourceContext)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            Resource = resource;
            CreateOnlyOnNoMatch = createOnlyOnNoMatch;
        }

        public ResourceElement Resource { get; }

        /// <summary>
        /// When true and the search finds no match, a resource with a client-supplied id is only created;
        /// the request fails with a conflict instead of overwriting a live resource with that id.
        /// When false (the default), the resource is upserted by id.
        /// </summary>
        public bool CreateOnlyOnNoMatch { get; }

        protected override IEnumerable<string> GetCapabilities() => Capabilities;
    }
}
