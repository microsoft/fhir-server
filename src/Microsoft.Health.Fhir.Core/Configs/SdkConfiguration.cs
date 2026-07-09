// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public enum FhirSdkMode
    {
        Firely,
        Ignixa,
        Hybrid,
    }

    public class SdkConfiguration
    {
        public FhirSdkMode Mode { get; set; } = FhirSdkMode.Hybrid;
    }
}
