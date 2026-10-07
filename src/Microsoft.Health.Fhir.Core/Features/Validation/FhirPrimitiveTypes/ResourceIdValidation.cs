// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;

namespace Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes
{
    /// <summary>
    /// Provides reusable whole-string resource id validation: a length check followed by a character scan.
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

        private static readonly SearchValues<char> IdCharacters = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.-");

        /// <summary>
        /// Gets the maximum resource id length for the selected mode.
        /// </summary>
        /// <param name="useLongResourceIds">Whether ids up to 128 characters are allowed.</param>
        /// <returns>The maximum resource id length, either 64 or 128.</returns>
        public static int GetMaxLength(bool useLongResourceIds) => useLongResourceIds ? LongMaxLength : StandardMaxLength;

        /// <summary>
        /// Determines whether a resource id is 1 to the mode's maximum length of letters, digits, dots and hyphens.
        /// </summary>
        /// <param name="resourceId">The resource id to validate.</param>
        /// <param name="useLongResourceIds">Whether ids up to 128 characters are allowed.</param>
        /// <returns>True when the id is valid.</returns>
        public static bool IsValid(ReadOnlySpan<char> resourceId, bool useLongResourceIds)
            => resourceId.Length > 0 && resourceId.Length <= GetMaxLength(useLongResourceIds) && !resourceId.ContainsAnyExcept(IdCharacters);
    }
}
