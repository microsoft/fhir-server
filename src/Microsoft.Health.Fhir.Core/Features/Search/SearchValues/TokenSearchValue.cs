// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents a token search value.
    /// </summary>
    public class TokenSearchValue : ISearchValue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TokenSearchValue"/> class.
        /// </summary>
        /// <param name="system">The token system value.</param>
        /// <param name="code">The token code value.</param>
        /// <param name="text">The display text.</param>
        public TokenSearchValue(string system, string code, string text)
        {
            // Either system or code has to exist.
            EnsureArg.IsTrue(
                !string.IsNullOrWhiteSpace(system) ||
                !string.IsNullOrWhiteSpace(code) ||
                !string.IsNullOrWhiteSpace(text));

            System = system;
            Code = code;
            Text = text;
        }

        /// <summary>
        /// Gets the token system value.
        /// </summary>
        public string System { get; }

        /// <summary>
        /// Gets the token code value.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the display text.
        /// </summary>
        public string Text { get; }

        /// <inheritdoc />
        public bool IsValidAsCompositeComponent =>
            !string.IsNullOrWhiteSpace(System) || !string.IsNullOrWhiteSpace(Code);

        /// <summary>
        /// Parses the string value to an instance of <see cref="TokenSearchValue"/>.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An instance of <see cref="TokenSearchValue"/>.</returns>
        public static TokenSearchValue Parse(string s)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

            IReadOnlyList<string> parts = s.SplitByTokenSeparator();

            if (parts.Count == 1)
            {
                // There was no separator, so this value represents the code.
                return new TokenSearchValue(
                    null,
                    parts[0].UnescapeSearchParameterValue(),
                    null);
            }
            else if (parts.Count == 2)
            {
                return new TokenSearchValue(
                    parts[0].UnescapeSearchParameterValue(),
                    parts[1].UnescapeSearchParameterValue(),
                    null);
            }
            else
            {
                throw new FormatException(Core.Resources.MoreThanOneTokenSeparatorSpecified);
            }
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
            if (System == null)
            {
                return Code.EscapeSearchParameterValue();
            }

            return $"{System.EscapeSearchParameterValue()}|{Code.EscapeSearchParameterValue()}";
        }
    }
}
