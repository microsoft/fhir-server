// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        /// <param name="number">The value that will be both the Low and High range</param>
        public NumberSearchValue(decimal number)
        : this(number, number)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NumberSearchValue"/> class.
        /// </summary>
        /// <param name="low">The lower bound of the quantity range.</param>
        /// <param name="high">The upper bound of the quantity range.</param>
        public NumberSearchValue(decimal? low, decimal? high)
        {
            if (low == null && high == null)
            {
                throw new ArgumentNullException(nameof(low), $"Arguments '{nameof(low)}' and '{nameof(high)}' cannot both be null");
            }

            Low = low;
            High = high;
        }

        /// <summary>
        /// Gets the lower bound
        /// </summary>
        public decimal? Low { get; }

        /// <summary>
        /// Gets the upper bound
        /// </summary>
        public decimal? High { get; }

        /// <inheritdoc />
        public bool IsValidAsCompositeComponent => true;

        /// <summary>
        /// Parses the string value to an instance of <see cref="NumberSearchValue"/>.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An instance of <see cref="NumberSearchValue"/>.</returns>
        public static NumberSearchValue Parse(string s)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

            // TODO: Is invariant culture correct? FHIR spec does not specify what culture it accepts for input.
            decimal value = decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture);
            return new NumberSearchValue(value);
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
            if (Low == High)
            {
                return Low.Value.ToString(CultureInfo.InvariantCulture);
            }

            return $"[{Low}, {High})";
        }
    }
}
