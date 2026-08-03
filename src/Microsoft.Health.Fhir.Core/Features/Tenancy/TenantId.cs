// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Identifies a single logical FHIR tenant.
    /// </summary>
    /// <remarks>
    /// A process that serves a single FHIR service uses <see cref="Default"/> everywhere. Comparison is ordinal
    /// case-insensitive because tenant identifiers are frequently derived from DNS labels.
    /// </remarks>
    public readonly struct TenantId : IEquatable<TenantId>
    {
        private const string DefaultValue = "(default)";

        private readonly string _value;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantId"/> struct.
        /// </summary>
        /// <param name="value">The tenant identifier. Must not be null, empty, or whitespace.</param>
        public TenantId(string value)
        {
            EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

            _value = value;
        }

        /// <summary>
        /// Gets the tenant used by a process that serves exactly one FHIR service.
        /// </summary>
        public static TenantId Default => new(DefaultValue);

        /// <summary>
        /// Gets the tenant identifier. A default-constructed <see cref="TenantId"/> reports the default tenant.
        /// </summary>
        public string Value => _value ?? DefaultValue;

        /// <summary>
        /// Determines whether two <see cref="TenantId"/> values are equal.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns><see langword="true"/> when the values are equal; otherwise, <see langword="false"/>.</returns>
        public static bool operator ==(TenantId left, TenantId right) => left.Equals(right);

        /// <summary>
        /// Determines whether two <see cref="TenantId"/> values are not equal.
        /// </summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns><see langword="true"/> when the values are not equal; otherwise, <see langword="false"/>.</returns>
        public static bool operator !=(TenantId left, TenantId right) => !left.Equals(right);

        /// <inheritdoc />
        public bool Equals(TenantId other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is TenantId other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

        /// <inheritdoc />
        public override string ToString() => Value;
    }
}
