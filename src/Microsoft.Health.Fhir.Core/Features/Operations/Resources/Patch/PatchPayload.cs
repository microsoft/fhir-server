// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Sdk;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    public abstract class PatchPayload
    {
        private readonly ISdkFallbackGuard _fallbackGuard;
        private readonly string _fallbackSurface;
        private readonly string _fallbackReason;

        protected PatchPayload()
        {
        }

        protected PatchPayload(ISdkFallbackGuard fallbackGuard, string fallbackSurface, string fallbackReason)
        {
            _fallbackGuard = fallbackGuard;
            _fallbackSurface = fallbackSurface;
            _fallbackReason = fallbackReason;
        }

        internal static ISet<string> ImmutableProperties =>
            new HashSet<string>
            {
                "Resource.id",
                "Resource.meta.lastUpdated",
                "Resource.meta.versionId",
                "Resource.text.div",
                "Resource.text.status",
            };

        internal abstract ResourceElement GetPatchedResourceElement(ResourceWrapper resourceToPatch);

        public ResourceElement Patch(ResourceWrapper resourceToPatch)
        {
            EnsureArg.IsNotNull(resourceToPatch, nameof(resourceToPatch));

            try
            {
                _fallbackGuard?.FirelyFallback(_fallbackSurface, _fallbackReason);
            }
            catch (InvalidOperationException e)
            {
                throw new RequestNotValidException(e.Message);
            }

            // Capture the state of properties that are immutable
            ITypedElement resource = resourceToPatch.RawResource.ToITypedElement(ModelInfoProvider.Instance);
            (string path, object result)[] preState = ImmutableProperties.Select(x => (path: x, result: resource.Scalar(x))).ToArray();

            // Get result of patch operation
            ResourceElement patchedResource = GetPatchedResourceElement(resourceToPatch);

            // Check if any immutable properties were changed
            (string path, object result)[] postState = ImmutableProperties.Select(x => (path: x, result: patchedResource.Scalar<object>(x))).ToArray();
            if (!preState.Zip(postState).All(x => x.First.path == x.Second.path && string.Equals(x.First.result?.ToString(), x.Second.result?.ToString(), StringComparison.Ordinal)))
            {
                throw new RequestNotValidException(Core.Resources.PatchImmutablePropertiesIsNotValid);
            }

            return patchedResource;
        }
    }
}
