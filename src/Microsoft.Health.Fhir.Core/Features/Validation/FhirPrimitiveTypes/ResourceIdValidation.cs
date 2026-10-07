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
        /// <summary>
        /// The FHIR specification limit, used when long resource ids are disabled.
        /// </summary>
        public const int StandardMaxLength = 64;

        /// <summary>
        /// The limit when long resource ids are enabled. This is the SQL Server resource id column width from schema version 118.
        /// </summary>
        public const int LongMaxLength = 128;

        private static readonly Regex StandardIdRegex = new Regex($@"\A[A-Za-z0-9.-]{{1,{StandardMaxLength}}}\z", RegexOptions.Compiled);
        private static readonly Regex LongIdRegex = new Regex($@"\A[A-Za-z0-9.-]{{1,{LongMaxLength}}}\z", RegexOptions.Compiled);

        /// <summary>
        /// Gets the maximum resource id length for the selected mode.
        /// </summary>
        /// <param name="useLongResourceIds">Whether ids up to 128 characters are allowed.</param>
        /// <returns>The maximum resource id length, either 64 or 128.</returns>
        public static int GetMaxLength(bool useLongResourceIds) => useLongResourceIds ? LongMaxLength : StandardMaxLength;

        /// <summary>
        /// Gets the compiled resource id expression for the selected mode.
        /// </summary>
        /// <param name="useLongResourceIds">Whether ids up to 128 characters are allowed.</param>
        /// <returns>The shared compiled expression.</returns>
        public static Regex GetRegex(bool useLongResourceIds) => useLongResourceIds ? LongIdRegex : StandardIdRegex;
    }
}
