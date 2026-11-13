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

        /// <summary>
        /// The longest base URI of an external reference that the reference index holds.
        /// </summary>
        public const int MaxReferenceBaseUriLength = 128;

        private static readonly SearchValues<char> IdCharacters = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.-");

        private ResourceIdPolicy(int maxLength, bool rejectLongReferences = true)
        {
            MaxLength = maxLength;
            RejectLongReferences = rejectLongReferences;
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
        /// Gets a value indicating whether a reference that is too long to index whole is rejected.
        /// When true, a typed reference whose id is longer than <see cref="MaxLength"/> is rejected, and so is a Reference element
        /// whose untyped value is longer than <see cref="MaxLength"/> or whose base URI is longer than <see cref="MaxReferenceBaseUriLength"/>.
        /// Other values that do not fit the SQL index are not indexed and cannot be matched.
        /// When false, values are cut off at their limits.
        /// </summary>
        public bool RejectLongReferences { get; }

        /// <summary>
        /// Selects the policy for the <c>UseLongResourceIds</c> and <c>RejectLongReferences</c> settings.
        /// </summary>
        /// <param name="useLongResourceIds">Whether ids up to 128 characters are allowed.</param>
        /// <param name="rejectLongReferences">Whether references that are too long to index whole are rejected or left unindexed, instead of cut off.</param>
        /// <returns>The matching policy.</returns>
        public static ResourceIdPolicy From(bool useLongResourceIds, bool rejectLongReferences = true)
            => new ResourceIdPolicy(useLongResourceIds ? LongMaxLength : StandardMaxLength, rejectLongReferences);

        /// <summary>
        /// Determines whether a resource id is 1 to <see cref="MaxLength"/> letters, digits, dots or hyphens.
        /// </summary>
        /// <param name="resourceId">The resource id to validate.</param>
        /// <returns>True when the id is valid.</returns>
        public bool IsValid(ReadOnlySpan<char> resourceId)
            => resourceId.Length > 0 && resourceId.Length <= MaxLength && !resourceId.ContainsAnyExcept(IdCharacters);
    }
}
