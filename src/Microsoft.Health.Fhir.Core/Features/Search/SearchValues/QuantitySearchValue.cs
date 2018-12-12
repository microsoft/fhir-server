// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents a quantity search value.
    /// </summary>
    public class QuantitySearchValue : ISearchValue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QuantitySearchValue"/> class.
        /// </summary>
        /// <param name="system">The system value.</param>
        /// <param name="code">The code value.</param>
        /// <param name="quantity">The quantity value.</param>
        public QuantitySearchValue(string system, string code, decimal quantity)
        {
            System = system;
            Code = code;
            Quantity = quantity;
        }

        /// <summary>
        /// Gets the system value.
        /// </summary>
        public string System { get; }

        /// <summary>
        /// Gets the code value.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the quantity value.
        /// </summary>
        public decimal Quantity { get; }

        /// <inheritdoc />
        public bool IsValidAsCompositeComponent => true;

        /// <summary>
        /// Parses the string value to an instance of <see cref="QuantitySearchValue"/>.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An instance of <see cref="QuantitySearchValue"/>.</returns>
        public static QuantitySearchValue Parse(string s)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

            IReadOnlyList<string> parts = s.SplitByTokenSeparator();

            if (parts.Count > 3)
            {
                throw new FormatException(Core.Resources.MoreThanTwoTokenSeparatorSpecified);
            }

            decimal quantity;
            if (!decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out quantity))
            {
                throw new BadRequestException(string.Format(Core.Resources.MalformedSearchValue, parts[0]));
            }

            string system = parts.Count > 1 ? parts[1] : string.Empty;
            string code = parts.Count > 2 ? parts[2] : string.Empty;

            return new QuantitySearchValue(
                system.UnescapeSearchParameterValue(),
                code.UnescapeSearchParameterValue(),
                quantity);
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
            var sb = new StringBuilder();

            sb.Append(Quantity);

            if (System != null)
            {
                sb.Append("|").Append(System.EscapeSearchParameterValue());
            }

            if (Code != null)
            {
                if (System == null)
                {
                    sb.Append("|");
                }

                sb.Append("|").Append(Code.EscapeSearchParameterValue());
            }

            return sb.ToString();
        }
    }
}
