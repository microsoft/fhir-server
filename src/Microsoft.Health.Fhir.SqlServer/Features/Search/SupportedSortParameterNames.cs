// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features
{
    /// <summary>
    /// Provides list of supported sort parameter names.
    /// At the moment this list is hard coded, but should be converted into a dynamic one.
    /// In addition, only Date time params are supported by the current implementation.
    /// </summary>
    public static class SupportedSortParameterNames
    {
        public static readonly string[] Names = { "birthdate", "date", "abatement-date", "onset-date", "issued", "created", "started", "authoredon" };
    }
}
