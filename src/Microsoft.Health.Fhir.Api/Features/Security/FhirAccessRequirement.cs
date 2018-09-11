// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;

namespace Microsoft.Health.Fhir.Api.Features.Security
{
    /// <summary>
    /// Represents the basic requirement for access to the FHIR server. Without meeting this requirement, no secured routes will be accessible.
    /// </summary>
    public class FhirAccessRequirement : IAuthorizationRequirement
    {
    }
}
