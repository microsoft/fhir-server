// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Patch
{
    public sealed class ConditionalPatchResourceRequest : ConditionalResourceRequest<UpsertResourceResponse>
    {
        private static readonly string[] Capabilities = new string[1] { "conditionalPatch = true" };

        public ConditionalPatchResourceRequest(
            string resourceType,
            PatchPayload payload,
            IReadOnlyList<Tuple<string, string>> conditionalParameters,
            bool maxParallelism = false,
            BundleResourceContext bundleResourceContext = null,
            WeakETag weakETag = null)
            : base(resourceType, conditionalParameters, maxParallelism, bundleResourceContext)
        {
            EnsureArg.IsNotNull(payload, nameof(payload));

            Payload = payload;
            WeakETag = weakETag;
        }

        public PatchPayload Payload { get; }

        public WeakETag WeakETag { get; }

        protected override IEnumerable<string> GetCapabilities() => Capabilities;
    }
}
