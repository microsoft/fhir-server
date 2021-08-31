// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents a identifer with :of-type modifier search value.
    /// </summary>
    public class IdentifierOfTypeSearchValue : ISearchValue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IdentifierOfType"/> class.
        /// </summary>
        /// <param name="system">The identifier system value.</param>
        /// <param name="code">The identifier code value.</param>
        /// <param name="value">The identifier value.</param>
        public IdentifierOfTypeSearchValue(string system, string code, string value)
        {
            EnsureArg.IsNotEmptyOrWhiteSpace(system, nameof(system));
            EnsureArg.IsNotEmptyOrWhiteSpace(system, nameof(code));
            EnsureArg.IsNotEmptyOrWhiteSpace(system, nameof(value));

            System = system;
            Code = code;
            Value = value;
        }

        /// <summary>
        /// Gets the identifier system value.
        /// </summary>
        public string System { get; }

        /// <summary>
        /// Gets the identifier code value.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the identifier value.
        /// </summary>
        public string Value { get; }

        /// <inheritdoc />
        public bool IsValidAsCompositeComponent => false;

        /// <summary>
        /// Parses the string value to an instance of <see cref="IdentifierOfTypeSearchValue"/>.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An instance of <see cref="IdentifierOfTypeSearchValue"/>.</returns>
        public static IdentifierOfTypeSearchValue Parse(string s)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

            IReadOnlyList<string> parts = s.SplitByTokenSeparator();

            if (parts.Count == 3)
            {
                return new IdentifierOfTypeSearchValue(parts[0], parts[1], parts[2]);
            }
            else
            {
                throw new FormatException(Resources.IdentifierOfTypeMustHaveThreeParams);
            }
        }

        /// <inheritdoc />
        public void AcceptVisitor(ISearchValueVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            visitor.Visit(this);
        }

        public bool Equals([AllowNull] ISearchValue other)
        {
            if (other == null)
            {
                return false;
            }

            var identifierSearchValueOther = other as IdentifierOfTypeSearchValue;

            if (identifierSearchValueOther == null)
            {
                return false;
            }

            return System.Equals(identifierSearchValueOther.System, StringComparison.Ordinal) &&
                   Code.Equals(identifierSearchValueOther.Code, StringComparison.Ordinal) &&
                   Value.Equals(identifierSearchValueOther.Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{System.EscapeSearchParameterValue()}|{Code.EscapeSearchParameterValue()}|{Value.EscapeSearchParameterValue()}";
        }
    }
}
