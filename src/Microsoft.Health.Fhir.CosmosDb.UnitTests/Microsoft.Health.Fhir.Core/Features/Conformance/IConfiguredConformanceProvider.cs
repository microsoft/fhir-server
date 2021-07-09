// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public interface IConfiguredConformanceProvider : IConformanceProvider
    {
        void ConfigureOptionalCapabilities(Action<ListedCapabilityStatement> builder);
    }
}
