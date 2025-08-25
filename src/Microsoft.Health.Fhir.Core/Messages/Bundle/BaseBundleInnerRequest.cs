// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Bundle
{
    public abstract class BaseBundleInnerRequest : IBundleInnerRequest
    {
        protected BaseBundleInnerRequest(BundleResourceContext bundleResourceContext)
        {
            BundleResourceContext = bundleResourceContext;
        }

        public BundleResourceContext BundleResourceContext { get; }

        public bool IsBundleInnerRequest => BundleResourceContext != null;
    }
}
