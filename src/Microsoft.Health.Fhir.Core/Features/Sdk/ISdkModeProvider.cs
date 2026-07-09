// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Sdk
{
    public interface ISdkModeProvider
    {
        FhirSdkMode Mode { get; }

        bool IsFirelyMode { get; }

        bool IsIgnixaMode { get; }

        bool IsHybridMode { get; }
    }
}
