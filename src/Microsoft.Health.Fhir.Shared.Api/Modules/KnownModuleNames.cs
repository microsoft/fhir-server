// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Api.Modules
{
    [Flags]
    public enum KnownModuleNames
    {
        None = 0,
        Anonymization = 1,
        Fhir = 1 << 1,
        Mediation = 1 << 2,
        Mvc = 1 << 3,
        Operations = 1 << 4,
        Persistence = 1 << 5,
        Search = 1 << 6,
        Validation = 1 << 7,
        All = Anonymization | Fhir | Mediation | Mvc | Operations | Persistence | Search | Validation,
    }
}
