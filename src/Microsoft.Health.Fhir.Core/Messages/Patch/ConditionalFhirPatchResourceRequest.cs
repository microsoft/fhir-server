// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Messages.Patch
{
    public sealed class ConditionalFhirPatchResourceRequest : ConditionalResourceRequest<UpsertResourceResponse>
    {
        private static readonly string[] Capabilities = new string[1] { "conditionalPatch = true" };

        public ConditionalFhirPatchResourceRequest(
            string resourceType,
            Parameters paramsResource,
            IReadOnlyList<Tuple<string, string>> conditionalParameters,
            WeakETag weakETag = null)
            : base(resourceType, conditionalParameters)
        {
            EnsureArg.IsNotNull(paramsResource, nameof(paramsResource));

            ParamsResource = paramsResource;
            WeakETag = weakETag;
        }

        public Parameters ParamsResource { get; }

        public WeakETag WeakETag { get; }

        protected override IEnumerable<string> GetCapabilities() => Capabilities;
    }
}
