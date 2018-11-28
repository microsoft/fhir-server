// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    public class CompartmentResourceResponse
    {
        public CompartmentResourceResponse(Bundle bundle)
        {
            EnsureArg.IsNotNull(bundle, nameof(bundle));

            Bundle = bundle;
        }

        public Bundle Bundle { get; }
    }
}
