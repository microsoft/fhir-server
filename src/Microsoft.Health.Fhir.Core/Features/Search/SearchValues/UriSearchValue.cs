// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents an URI search value.
    /// </summary>
    public class UriSearchValue : ISearchValue
    {
        private const string UrlGroupName = "url";
        private const string FragmentGroupName = "fragment";
        private const string VersionGroupName = "version";

        private static readonly Regex _canonicalFormat = new Regex($"^(?<{UrlGroupName}>[\\w\\-\\._~:/?[\\]@!\\$&'\\(\\)\\*\\+,;=.]+)(?<{VersionGroupName}>\\|[\\w\\-\\._:]+)?(?<{FragmentGroupName}>\\#[\\w\\-\\._:]+)?$", RegexOptions.Compiled);

        /// <summary>
        /// Initializes a new instance of the <see cref="UriSearchValue"/> class.
        /// </summary>
        /// <param name="uri">The URI value.</param>
        /// <param name="separateCanonicalComponents">When true, the Version and Fragment will be separated into Canonical components</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "Value is passed in from user")]
        public UriSearchValue(string uri, bool separateCanonicalComponents)
        {
            EnsureArg.IsNotNullOrWhiteSpace(uri, nameof(uri));
            Match result;

            if (!separateCanonicalComponents || !(result = _canonicalFormat.Match(uri)).Success)
            {
                Uri = uri;
                return;
            }

            // More information: https://www.hl7.org/fhir/references.html#canonical-fragments
            // Parse url in format of:
            // http://example.com/folder|4#fragment

            Uri = ValueWhenNotNullOrWhiteSpace(result.Groups[UrlGroupName].Value);
            Fragment = ValueWhenNotNullOrWhiteSpace(result.Groups[FragmentGroupName].Value?.TrimStart('#'));
            Version = ValueWhenNotNullOrWhiteSpace(result.Groups[VersionGroupName].Value?.TrimStart('|'));

            string ValueWhenNotNullOrWhiteSpace(string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the URI value.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI properties should not be strings", Justification = "Value is passed in from user")]
        public string Uri { get; protected set; }

        public string Version { get; set; }

        public string Fragment { get; set; }

        /// <summary>
        /// When true the search value has Canonical components Uri, Version and/or Fragment.
        /// When false the search value contains only Uri.
        /// </summary>
        public bool IsCanonical
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Version) || !string.IsNullOrWhiteSpace(Fragment);
            }
        }

        /// <inheritdoc />
        public bool IsValidAsCompositeComponent => true;

        /// <summary>
        /// Parses the string value to an instance of <see cref="UriSearchValue"/>.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <param name="separateCanonicalComponents">When true, the Version and Fragment will be separated into Canonical components</param>
        /// <param name="modelInfoProvider">FHIR Model Info provider</param>
        /// <returns>An instance of <see cref="UriSearchValue"/>.</returns>
        public static UriSearchValue Parse(string s, bool separateCanonicalComponents, IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            if (modelInfoProvider.Version == FhirSpecification.Stu3 || !_canonicalFormat.IsMatch(s))
            {
                return new UriSearchValue(s, false);
            }

            return new UriSearchValue(s, separateCanonicalComponents);
        }

        /// <inheritdoc />
        public virtual void AcceptVisitor(ISearchValueVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            visitor.Visit(this);
        }

        public virtual bool Equals([AllowNull] ISearchValue other)
        {
            var uriSearchValueOther = other as UriSearchValue;

            if (uriSearchValueOther == null)
            {
                return false;
            }

            // URLs are always considered to be case-sensitive (https://www.hl7.org/fhir/references.html#literal)
            return string.Equals(ToString(), uriSearchValueOther.ToString(), StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var asString = new StringBuilder(Uri);

            if (!string.IsNullOrEmpty(Version))
            {
                asString.AppendFormat(CultureInfo.InvariantCulture, "|{0}", Version);
            }

            if (!string.IsNullOrEmpty(Fragment))
            {
                asString.AppendFormat(CultureInfo.InvariantCulture, "#{0}", Fragment);
            }

            return asString.ToString();
        }
    }
}
