// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Internal.Fhir.RegisterAndMonitorImport
{
    public enum VerbosityLevel
    {
        Full = 5,
        FullOnComplete = 4,
        Error = 3,
        ErrorOnComplete = 2,
        Minimal = 1,
        None = 0,
    }
}
