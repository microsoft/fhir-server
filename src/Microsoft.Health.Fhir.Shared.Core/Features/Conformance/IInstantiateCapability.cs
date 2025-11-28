// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Conformance
{
    public interface IInstantiateCapability
    {
        bool TryGetUrls(out IEnumerable<string> urls);
    }
}
