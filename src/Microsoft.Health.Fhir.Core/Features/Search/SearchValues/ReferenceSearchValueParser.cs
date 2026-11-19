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
using Microsoft.Health.Fhir.Core.Registration;

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
        private static readonly string ReferenceCaptureRegexPattern = $@"(?<{ResourceTypeCapture}>{ResourceTypesPattern})\/(?<{ResourceIdCapture}>[A-Za-z0-9\-\.]+)(\/_history\/[A-Za-z0-9\-\.]{{1,64}})?";

        private static readonly Regex ReferenceRegex = new Regex(
            ReferenceCaptureRegexPattern,
            RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IFhirServerInstanceConfiguration _instanceConfiguration;
        private readonly ResourceIdPolicy _resourceIdPolicy;
        private readonly bool _bareIdsTruncatedWhenIndexed;

        public ReferenceSearchValueParser(
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IFhirServerInstanceConfiguration instanceConfiguration,
            ResourceIdPolicy resourceIdPolicy)
            : this(fhirRequestContextAccessor, instanceConfiguration, resourceIdPolicy, runtimeConfiguration: null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReferenceSearchValueParser"/> class.
        /// </summary>
        /// <param name="fhirRequestContextAccessor">The request context accessor.</param>
        /// <param name="instanceConfiguration">The server instance configuration.</param>
        /// <param name="resourceIdPolicy">The resource id policy.</param>
        /// <param name="runtimeConfiguration">
        /// The runtime configuration. Bare reference ids (without a resource type) are flagged as truncated only when the data store
        /// truncates them when indexing: SQL Server does, Cosmos DB stores the full value. <c>null</c> assumes SQL Server.
        /// </param>
        public ReferenceSearchValueParser(
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IFhirServerInstanceConfiguration instanceConfiguration,
            ResourceIdPolicy resourceIdPolicy,
            IFhirRuntimeConfiguration runtimeConfiguration)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(instanceConfiguration, nameof(instanceConfiguration));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _instanceConfiguration = instanceConfiguration;
            _resourceIdPolicy = EnsureArg.IsNotNull(resourceIdPolicy, nameof(resourceIdPolicy));
            _bareIdsTruncatedWhenIndexed = runtimeConfiguration == null || !KnownDataStores.IsCosmosDbDataStore(runtimeConfiguration.DataStore);
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
                int? overlongResourceIdLength = null;

                // Ids longer than the limit are cut off at the limit, as when the limit was fixed at 64.
                // The original length is kept so that callers can log and warn about the truncation.
                if (resourceId.Length > _resourceIdPolicy.MaxLength)
                {
                    overlongResourceIdLength = resourceId.Length;
                    resourceId = resourceId[.._resourceIdPolicy.MaxLength];
                }

                int resourceTypeStartIndex = match.Groups[ResourceTypeCapture].Index;

                if (resourceTypeStartIndex == 0)
                {
                    // This is relative URL.
                    return new ReferenceSearchValue(
                        ReferenceKind.InternalOrExternal,
                        null,
                        resourceTypeInString,
                        resourceId)
                    {
                        OverlongResourceIdLength = overlongResourceIdLength,
                        MaxResourceIdLength = overlongResourceIdLength == null ? null : _resourceIdPolicy.MaxLength,
                    };
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
                            resourceId)
                        {
                            OverlongResourceIdLength = overlongResourceIdLength,
                            MaxResourceIdLength = overlongResourceIdLength == null ? null : _resourceIdPolicy.MaxLength,
                        };
                    }
                    else if (baseUri.IsAbsoluteUri &&
                        SupportedSchemes.Contains(baseUri.Scheme, StringComparer.OrdinalIgnoreCase))
                    {
                        // This is an absolute URL pointing to an external resource.
                        return new ReferenceSearchValue(
                            ReferenceKind.External,
                            baseUri,
                            resourceTypeInString,
                            resourceId)
                        {
                            OverlongResourceIdLength = overlongResourceIdLength,
                            MaxResourceIdLength = overlongResourceIdLength == null ? null : _resourceIdPolicy.MaxLength,
                        };
                    }
                }
                catch (UriFormatException)
                {
                    // The reference is not a relative reference but is not a valid absolute reference either.
                }
            }

            // A bare id (no resource type) isn't truncated here. SQL Server truncates it to the same limit when indexed;
            // Cosmos DB stores and compares the full value, so it isn't flagged there.
            bool isOverlongBareId = _bareIdsTruncatedWhenIndexed && _resourceIdPolicy.IsTooLong(s);
            return new ReferenceSearchValue(
                ReferenceKind.InternalOrExternal,
                baseUri: null,
                resourceType: null,
                resourceId: s)
            {
                OverlongResourceIdLength = isOverlongBareId ? s.Length : null,
                MaxResourceIdLength = isOverlongBareId ? _resourceIdPolicy.MaxLength : null,
            };
        }
    }
}
