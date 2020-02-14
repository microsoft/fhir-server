﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Security.Authorization
{
    internal class DisabledFhirAuthorizationService : IFhirAuthorizationService
    {
        public static readonly DisabledFhirAuthorizationService Instance = new DisabledFhirAuthorizationService();

        public FhirActions CheckAccess(FhirActions actions)
        {
            return actions;
        }
    }
}
