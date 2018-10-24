// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents an URI search value.
    /// </summary>
    public class UriSearchValue : ISearchValue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UriSearchValue"/> class.
        /// </summary>
        /// <param name="uri">The URI value.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "Value is passed in from user")]
        public UriSearchValue(string uri)
        {
            EnsureArg.IsNotNullOrWhiteSpace(uri, nameof(uri));

            Uri = uri;
        }

        /// <summary>
        /// Gets the URI value.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI properties should not be strings", Justification = "Value is passed in from user")]
        public string Uri { get; }

        /// <inheritdoc />
        public bool IsValidAsCompositeComponent => true;

        /// <summary>
        /// Parses the string value to an instance of <see cref="UriSearchValue"/>.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An instance of <see cref="UriSearchValue"/>.</returns>
        public static UriSearchValue Parse(string s)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

            return new UriSearchValue(s);
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
            return Uri;
        }
    }
}
