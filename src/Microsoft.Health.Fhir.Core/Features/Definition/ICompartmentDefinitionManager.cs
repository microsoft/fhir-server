// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    public interface ICompartmentDefinitionManager
    {
        bool TryGetSearchParams(string resourceType, string compartmentType, out HashSet<string> searchParams);
    }
}
