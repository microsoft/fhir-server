// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text.RegularExpressions;
using EnsureThat;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Provides mechanism to parse a string to an instance of <see cref="ReferenceSearchValue"/>.
    /// </summary>
    /// <remarks>
    /// A recognized resource id is always the complete path segment after the resource type. It is either
    /// accepted whole under the <see cref="ResourceIdPolicy"/> or rejected; it is never shortened.
    /// </remarks>
    public class ReferenceSearchValueParser : IReferenceSearchValueParser
    {
        private const string ResourceTypeCapture = "resourceType";
        private const string ResourceIdCapture = "resourceId";
        private static readonly string[] SupportedSchemes = new string[] { Uri.UriSchemeHttps, Uri.UriSchemeHttp };
        private static readonly string ResourceTypesPattern = string.Join('|', ModelInfoProvider.GetResourceTypeNames());

        // The id segment ends at the next path, query, canonical version or fragment delimiter.
        private static readonly string ReferenceCaptureRegexPattern = $@"(?<{ResourceTypeCapture}>{ResourceTypesPattern})\/(?<{ResourceIdCapture}>[^\/?|#]+)";

        private static readonly Regex ReferenceRegex = new Regex(
            ReferenceCaptureRegexPattern,
            RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IFhirServerInstanceConfiguration _instanceConfiguration;
        private readonly ResourceIdPolicy _resourceIdPolicy;

        public ReferenceSearchValueParser(
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IFhirServerInstanceConfiguration instanceConfiguration,
            ResourceIdPolicy resourceIdPolicy)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(instanceConfiguration, nameof(instanceConfiguration));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _instanceConfiguration = instanceConfiguration;
            _resourceIdPolicy = EnsureArg.IsNotNull(resourceIdPolicy, nameof(resourceIdPolicy));
        }

        /// <inheritdoc />
        public ReferenceSearchValue Parse(string value, ReferenceParseContext context)
        {
            EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));
            EnsureArg.EnumIsDefined(context, nameof(context));

            if (context == ReferenceParseContext.ResourceIdPrimitive)
            {
                return ReferenceSearchValue.FromResource(
                    ReferenceKind.InternalOrExternal,
                    baseUri: null,
                    resourceType: null,
                    BoundedResourceId.Parse(value, _resourceIdPolicy));
            }

            Match match = ReferenceRegex.Match(value);

            if (!match.Success)
            {
                return ParseUntypedValue(value, context);
            }

            string resourceType = match.Groups[ResourceTypeCapture].Value;
            ModelInfoProvider.EnsureValidResourceType(resourceType, nameof(value));

            int resourceTypeStartIndex = match.Groups[ResourceTypeCapture].Index;
            ReferenceKind kind;
            Uri baseUri = null;

            if (resourceTypeStartIndex == 0)
            {
                // This is relative URL.
                kind = ReferenceKind.InternalOrExternal;
            }
            else if (!TryClassifyBase(value[..resourceTypeStartIndex], out kind, out baseUri))
            {
                // Not a REST location, so the complete value is matched as an opaque literal.
                return ReferenceSearchValue.FromLiteral(value);
            }

            string resourceId = match.Groups[ResourceIdCapture].Value;

            if (context == ReferenceParseContext.CanonicalOrUri && !ResourceIdPolicy.HasIdGrammar(resourceId))
            {
                // A canonical or URI whose segment is not id-shaped is kept as a complete literal.
                return ReferenceSearchValue.FromLiteral(value);
            }

            return ReferenceSearchValue.FromResource(kind, baseUri, resourceType, BoundedResourceId.Parse(resourceId, _resourceIdPolicy));
        }

        private ReferenceSearchValue ParseUntypedValue(string value, ReferenceParseContext context)
        {
            if (context != ReferenceParseContext.CanonicalOrUri && ResourceIdPolicy.HasIdGrammar(value))
            {
                // A bare id in a reference is held to the same bound as a typed reference.
                return ReferenceSearchValue.FromResource(
                    ReferenceKind.InternalOrExternal,
                    baseUri: null,
                    resourceType: null,
                    BoundedResourceId.Parse(value, _resourceIdPolicy));
            }

            return ReferenceSearchValue.FromLiteral(value);
        }

        private bool TryClassifyBase(string baseValue, out ReferenceKind kind, out Uri baseUri)
        {
            kind = ReferenceKind.InternalOrExternal;
            baseUri = null;

            Uri candidate;

            try
            {
                candidate = new Uri(baseValue, UriKind.RelativeOrAbsolute);
            }
            catch (UriFormatException)
            {
                // The reference is not a relative reference but is not a valid absolute reference either.
                return false;
            }

            // Get the base URI from request context first, then fall back to global instance configuration
            // This ensures background operations like reindexing can access the base URI when there's no active HTTP context
            Uri contextBaseUri = _fhirRequestContextAccessor?.RequestContext?.BaseUri ?? _instanceConfiguration?.BaseUri;

            if (contextBaseUri != null && candidate == contextBaseUri)
            {
                // This is an absolute URL pointing to an internal resource.
                kind = ReferenceKind.Internal;
                return true;
            }

            if (candidate.IsAbsoluteUri &&
                SupportedSchemes.Contains(candidate.Scheme, StringComparer.OrdinalIgnoreCase))
            {
                // This is an absolute URL pointing to an external resource.
                kind = ReferenceKind.External;
                baseUri = candidate;
                return true;
            }

            return false;
        }
    }
}
