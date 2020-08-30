// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features
{
    /// <summary>
    /// Provides list of supported sort parameter names.
    /// </summary>
    public static class SupportedSortParameterNames
    {
        public static readonly string[] Names = { "birthdate", "date", "abatement-date", "onset-date", "issued", "created", "started", "authoredon" };
    }
}
