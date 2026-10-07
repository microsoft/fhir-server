// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text.RegularExpressions;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
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

        private readonly Regex _referenceRegex;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IFhirServerInstanceConfiguration _instanceConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReferenceSearchValueParser"/> class.
        /// </summary>
        /// <param name="fhirRequestContextAccessor">The current request context accessor.</param>
        /// <param name="instanceConfiguration">The server instance configuration.</param>
        /// <param name="coreFeatureConfiguration">The core feature configuration.</param>
        public ReferenceSearchValueParser(
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IFhirServerInstanceConfiguration instanceConfiguration,
            IOptions<CoreFeatureConfiguration> coreFeatureConfiguration)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(instanceConfiguration, nameof(instanceConfiguration));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _instanceConfiguration = instanceConfiguration;

            int maxResourceIdLength = ResourceIdValidation.GetMaxLength(EnsureArg.IsNotNull(coreFeatureConfiguration?.Value, nameof(coreFeatureConfiguration)).UseLongResourceIds);

            // Same pattern as before long ids: a 1-64 (or 1-128) character id is captured and anything longer is cut off at the limit.
            string referenceCaptureRegexPattern = $@"(?<{ResourceTypeCapture}>{ResourceTypesPattern})\/(?<{ResourceIdCapture}>[A-Za-z0-9\-\.]{{1,{maxResourceIdLength}}})(\/_history\/[A-Za-z0-9\-\.]{{1,64}})?";

            // The parser is registered as a singleton; compile the selected pattern once and reuse it in Parse.
            _referenceRegex = new Regex(
                referenceCaptureRegexPattern,
                RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.ExplicitCapture);
        }

        /// <inheritdoc />
        public ReferenceSearchValue Parse(string s)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

            Match match = _referenceRegex.Match(s);

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

                    // Get the base URI from request context first, then fall back to global instance configuration
                    // This ensures background operations like reindexing can access the base URI when there's no active HTTP context
                    var requestContext = _fhirRequestContextAccessor?.RequestContext;
                    var contextBaseUri = requestContext?.BaseUri ?? _instanceConfiguration?.BaseUri;

                    if (contextBaseUri != null && baseUri == contextBaseUri)
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
