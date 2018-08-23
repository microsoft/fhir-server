// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents a reference search value.
    /// </summary>
    public class ReferenceSearchValue : ISearchValue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReferenceSearchValue"/> class.
        /// </summary>
        /// <param name="reference">The reference value.</param>
        public ReferenceSearchValue(string reference)
        {
            EnsureArg.IsNotNullOrWhiteSpace(reference, nameof(reference));

            Reference = reference;
        }

        /// <summary>
        /// Gets the reference value.
        /// </summary>
        public string Reference { get; }

        /// <summary>
        /// Parses the string value to an instance of <see cref="ReferenceSearchValue"/>.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An instance of <see cref="ReferenceSearchValue"/>.</returns>
        public static ReferenceSearchValue Parse(string s)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

            // TODO: Add logic to normalize the internal URL.
            // (e.g., change to relative URL if it's internal resource).
            return new ReferenceSearchValue(s);
        }

        /// <inheritdoc />
        public void AcceptVisitor(ISearchValueVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Reference;
        }
    }
}
