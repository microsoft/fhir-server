// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes
{
    /// <summary>
    /// A complete resource id that satisfies the active <see cref="ResourceIdPolicy"/>.
    /// </summary>
    public sealed class BoundedResourceId
    {
        private BoundedResourceId(string value)
        {
            Value = value;
        }

        /// <summary>
        /// Gets the complete resource id.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Validates a complete resource id against the policy.
        /// </summary>
        /// <param name="value">The resource id.</param>
        /// <param name="policy">The active resource id policy.</param>
        /// <returns>The validated resource id.</returns>
        /// <exception cref="ResourceIdentityFormatException">The value is not a valid resource id under the policy.</exception>
        public static BoundedResourceId Parse(string value, ResourceIdPolicy policy)
        {
            EnsureArg.IsNotNull(policy, nameof(policy));

            if (value == null || !policy.IsValid(value))
            {
                throw new ResourceIdentityFormatException(policy.MaxLength);
            }

            return new BoundedResourceId(value);
        }

        /// <inheritdoc />
        public override string ToString() => Value;
    }
}
