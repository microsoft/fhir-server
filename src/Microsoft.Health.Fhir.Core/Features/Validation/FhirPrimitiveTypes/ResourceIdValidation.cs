// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes
{
    /// <summary>
    /// Provides reusable whole-string resource id validation for standard and long ids.
    /// </summary>
    public static class ResourceIdValidation
    {
        private static readonly Regex StandardIdRegex = new Regex(@"\A[A-Za-z0-9.-]{1,64}\z", RegexOptions.Compiled);
        private static readonly Regex LongIdRegex = new Regex(@"\A[A-Za-z0-9.-]{1,128}\z", RegexOptions.Compiled);

        /// <summary>
        /// Gets the compiled resource id expression for the selected mode.
        /// </summary>
        /// <param name="useLongResourceIds">Whether ids up to 128 characters are allowed.</param>
        /// <returns>The shared compiled expression.</returns>
        public static Regex GetRegex(bool useLongResourceIds) => useLongResourceIds ? LongIdRegex : StandardIdRegex;
    }
}
