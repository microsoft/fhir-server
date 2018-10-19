// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents a number search value.
    /// </summary>
    public class NumberSearchValue : ISearchValue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NumberSearchValue"/> class.
        /// </summary>
        /// <param name="number">The number value.</param>
        public NumberSearchValue(decimal number) => Number = number;

        /// <summary>
        /// Gets the number value.
        /// </summary>
        public decimal Number { get; }

        /// <inheritdoc />
        public bool IsValidAsCompositeComponent { get; } = true;

        /// <summary>
        /// Parses the string value to an instance of <see cref="NumberSearchValue"/>.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An instance of <see cref="NumberSearchValue"/>.</returns>
        public static NumberSearchValue Parse(string s)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

            // TODO: Is invariant culture correct? FHIR spec does not specify what culture it accepts for input.
            return new NumberSearchValue(decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture));
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
            return Number.ToString(CultureInfo.InvariantCulture);
        }
    }
}
