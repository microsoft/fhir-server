// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Sdk
{
    public class SdkModeProvider : ISdkModeProvider
    {
        public SdkModeProvider(SdkConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            if (!Enum.IsDefined(configuration.Mode))
            {
                throw new InvalidOperationException($"Unsupported FHIR SDK mode: {configuration.Mode}.");
            }

            Mode = configuration.Mode;
        }

        public FhirSdkMode Mode { get; }

        public bool IsFirelyMode => Mode == FhirSdkMode.Firely;

        public bool IsIgnixaMode => Mode == FhirSdkMode.Ignixa;

        public bool IsHybridMode => Mode == FhirSdkMode.Hybrid;
    }
}
