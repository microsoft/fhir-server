// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text.RegularExpressions;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Provides mechanism to parse a string to an instance of <see cref="ReferenceSearchValue"/>.
    /// </summary>
    public class ReferenceSearchValueParser : IReferenceSearchValueParser
    {
        private const string ResourceTypeCapture = "resourceType";
        private const string ResourceIdCapture = "resourceId";
        private static readonly string[] SupportedSchemes = new string[] { Uri.UriSchemeHttps, Uri.UriSchemeHttp };
        private static readonly string ResourceTypesPattern = string.Join('|', ModelInfoProvider.GetResourceTypeNames());
        private static readonly string ReferenceCaptureRegexPattern = $@"(?<{ResourceTypeCapture}>{ResourceTypesPattern})\/(?<{ResourceIdCapture}>[A-Za-z0-9\-\.]{{1,64}})(\/_history\/[A-Za-z0-9\-\.]{{1,64}})?";

        private static readonly Regex ReferenceRegex = new Regex(
            ReferenceCaptureRegexPattern,
            RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;

        public ReferenceSearchValueParser(IFhirRequestContextAccessor fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
        }

        /// <inheritdoc />
        public ReferenceSearchValue Parse(string s)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

            Match match = ReferenceRegex.Match(s);

            if (match.Success)
            {
                string resourceTypeInString = match.Groups[ResourceTypeCapture].Value;

                ModelInfoProvider.EnsureValidResourceType(resourceTypeInString, nameof(s));

                string resourceId = match.Groups[ResourceIdCapture].Value;

                int resourceTypeStartIndex = match.Groups[ResourceTypeCapture].Index;

                if (resourceTypeStartIndex == 0)
                {
                    // This is relative URL.
                    return new ReferenceSearchValue(
                        ReferenceKind.InternalOrExternal,
                        null,
                        resourceTypeInString,
                        resourceId);
                }

                Uri baseUri = null;

                try
                {
                    baseUri = new Uri(s.Substring(0, resourceTypeStartIndex), UriKind.RelativeOrAbsolute);

                    if (baseUri == _fhirRequestContextAccessor.FhirRequestContext.BaseUri)
                    {
                        // This is an absolute URL pointing to an internal resource.
                        return new ReferenceSearchValue(
                            ReferenceKind.Internal,
                            null,
                            resourceTypeInString,
                            resourceId);
                    }
                    else if (baseUri.IsAbsoluteUri &&
                        SupportedSchemes.Contains(baseUri.Scheme, StringComparer.OrdinalIgnoreCase))
                    {
                        // This is an absolute URL pointing to an external resource.
                        return new ReferenceSearchValue(
                            ReferenceKind.External,
                            baseUri,
                            resourceTypeInString,
                            resourceId);
                    }
                }
                catch (UriFormatException)
                {
                    // The reference is not a relative reference but is not a valid absolute reference either.
                }
            }

            return new ReferenceSearchValue(
                ReferenceKind.InternalOrExternal,
                baseUri: null,
                resourceType: null,
                resourceId: s);
        }
    }
}
