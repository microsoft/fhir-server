// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using Azure.Identity;

namespace Microsoft.Health.Fhir.Client
{
    public sealed class FhirBundleOptions
    {
#pragma warning disable IDE1006 // Naming Styles
        public static readonly FhirBundleOptions Default = new();
#pragma warning restore IDE1006 // Naming Styles

        public FhirBundleOptions()
        {
            BundleProcessingLogic = FhirBundleProcessingLogic.Parallel;
            MaximizeConditionalQueryParallelism = false;
            ProfileValidation = false;
        }

        public FhirBundleProcessingLogic BundleProcessingLogic { get; set; }

        public bool MaximizeConditionalQueryParallelism { get; set; }

        public bool ProfileValidation { get; set; }
    }
}
