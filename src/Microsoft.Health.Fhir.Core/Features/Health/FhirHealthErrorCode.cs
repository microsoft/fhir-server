// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Health
{
    public enum FhirHealthErrorCode
    {
        Error000, // Not expected exceptions.
        Error001, // External cancellation requested.
        Error002, // Operation canceled.
        Error003, // Customer Managed Key is not available.
        Error004, // Rate-limit exceptions.
    }
}
