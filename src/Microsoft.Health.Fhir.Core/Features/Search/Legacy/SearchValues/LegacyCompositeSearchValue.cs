// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    /// <summary>
    /// Represents a composite search value.
    /// </summary>
    public class LegacyCompositeSearchValue : ISearchValue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LegacyCompositeSearchValue"/> class.
        /// </summary>
        /// <param name="system">The composite system value.</param>
        /// <param name="code">The composite code value.</param>
        /// <param name="value">The composite value.</param>
        public LegacyCompositeSearchValue(string system, string code, ISearchValue value)
        {
            EnsureArg.IsNotNull(value, nameof(value));

            System = system;
            Code = code;
            Value = value;
        }

        /// <summary>
        /// Gets the composite system value.
        /// </summary>
        public string System { get; }

        /// <summary>
        /// Gets the composite code value.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the composite value.
        /// </summary>
        public ISearchValue Value { get; }

        /// <inheritdoc />
        public bool IsValidAsCompositeComponent { get; } = false;

        /// <summary>
        /// Parses the string value to an instance of <see cref="CompositeSearchValue"/>.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <param name="parser">The parser used to parse the composite value.</param>
        /// <returns>An instance of <see cref="CompositeSearchValue"/>.</returns>
        public static LegacyCompositeSearchValue Parse(string s, SearchParamValueParser parser)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));
            EnsureArg.IsNotNull(parser, nameof(parser));

            IReadOnlyList<string> parts = s.SplitByCompositeSeparator();

            TokenSearchValue token = TokenSearchValue.Parse(parts[0]);

            ISearchValue value = parser(parts[1]);

            return new LegacyCompositeSearchValue(token.System, token.Code, value);
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

            if (System != null)
            {
                sb.Append(System.EscapeSearchParameterValue()).Append("|");
            }

            sb.Append(Code.EscapeSearchParameterValue()).Append("$").Append(Value);

            return sb.ToString();
        }
    }
}
