// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;

namespace Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes
{
    /// <summary>
    /// The resource id length limit and the validation of ids against it.
    /// </summary>
    public sealed class ResourceIdPolicy
    {
        /// <summary>
        /// The FHIR specification limit, used when long resource ids are disabled.
        /// </summary>
        public const int StandardMaxLength = 64;

        /// <summary>
        /// The limit when long resource ids are enabled.
        /// </summary>
        public const int LongMaxLength = 128;

        private static readonly SearchValues<char> IdCharacters = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.-");

        private ResourceIdPolicy(int maxLength)
        {
            MaxLength = maxLength;
        }

        /// <summary>
        /// Gets the policy with the 64 character FHIR specification limit.
        /// </summary>
        public static ResourceIdPolicy Standard { get; } = new ResourceIdPolicy(StandardMaxLength);

        /// <summary>
        /// Gets the policy with the 128 character limit.
        /// </summary>
        public static ResourceIdPolicy Extended { get; } = new ResourceIdPolicy(LongMaxLength);

        /// <summary>
        /// Gets the maximum resource id length, either 64 or 128.
        /// </summary>
        public int MaxLength { get; }

        /// <summary>
        /// Selects the policy for the <c>UseLongResourceIds</c> setting.
        /// </summary>
        /// <param name="useLongResourceIds">Whether ids up to 128 characters are allowed.</param>
        /// <returns>The matching policy.</returns>
        public static ResourceIdPolicy From(bool useLongResourceIds) => useLongResourceIds ? Extended : Standard;

        /// <summary>
        /// Determines whether a resource id is 1 to <see cref="MaxLength"/> letters, digits, dots or hyphens.
        /// </summary>
        /// <param name="resourceId">The resource id to validate.</param>
        /// <returns>True when the id is valid.</returns>
        public bool IsValid(ReadOnlySpan<char> resourceId)
            => resourceId.Length > 0 && resourceId.Length <= MaxLength && !resourceId.ContainsAnyExcept(IdCharacters);
    }
}
