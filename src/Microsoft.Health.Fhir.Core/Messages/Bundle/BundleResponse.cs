// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Bundle
{
    public class BundleResponse
    {
        public BundleResponse(ResourceElement bundle, BundleProcessingStatus processingStatus = BundleProcessingStatus.SUCCEEDED)
        {
            EnsureArg.IsNotNull(bundle, nameof(bundle));

            Bundle = bundle;

            BundleProcessingStatus = processingStatus;
        }

        public ResourceElement Bundle { get; }

        public BundleProcessingStatus BundleProcessingStatus { get; }
    }
}
