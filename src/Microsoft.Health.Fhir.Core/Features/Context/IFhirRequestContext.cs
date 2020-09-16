// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public interface IFhirRequestContext : IRequestContext
    {
        string ResourceType { get; set; }

        bool IncludePartiallyIndexedSearchParams { get; set; }
    }
}
