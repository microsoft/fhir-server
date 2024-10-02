// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Health
{
    public enum FhirHealthErrorCode
    {
        Error408, // External cancellation requested.
        Error412, // Customer Managed Key is not available.
        Error429, // Rate-limit exceptions.
        Error500, // Not expected exceptions.
        Error501, // Operation canceled.
    }
}
