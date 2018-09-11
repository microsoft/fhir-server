// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    /// <summary>
    /// Provides information about a resource.
    /// </summary>
    public class ResourceTypeManifest
    {
        private Dictionary<string, SearchParam> _supportedSearchParams;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceTypeManifest"/> class.
        /// </summary>
        /// <param name="resourceType">The reousrce type.</param>
        /// <param name="supportedSearchParams">A collection of supported search parameters.</param>
        public ResourceTypeManifest(Type resourceType, IReadOnlyCollection<SearchParam> supportedSearchParams)
        {
            EnsureArg.IsNotNull(resourceType, nameof(resourceType));
            EnsureArg.IsTrue(typeof(Resource).IsAssignableFrom(resourceType), nameof(resourceType));
            EnsureArg.IsNotNull(supportedSearchParams, nameof(supportedSearchParams));
            EnsureArg.IsTrue(supportedSearchParams.Any(), nameof(supportedSearchParams));

            ResourceType = resourceType;

            _supportedSearchParams = supportedSearchParams.ToDictionary(
                sp => sp.ParamName,
                sp => sp,
                StringComparer.Ordinal);
        }

        /// <summary>
        /// Gets the resource type.
        /// </summary>
        public Type ResourceType { get; }

        /// <summary>
        /// Gets a collection of the supported search parameters.
        /// </summary>
        public IEnumerable<SearchParam> SupportedSearchParams => _supportedSearchParams.Values.OrderBy(sp => sp.ParamName);

        /// <summary>
        /// Gets an instance of <see cref="SearchParam"/> representing the search parameter with <paramref name="paramName"/>.
        /// </summary>
        /// <param name="paramName">The search parameter name.</param>
        /// <returns>An instance of <see cref="SearchParam"/> representing the search parameter with <paramref name="paramName"/></returns>
        /// <exception cref="SearchParameterNotSupportedException">Throws when <paramref name="paramName"/> is not supported for this resource.</exception>
        public SearchParam GetSearchParam(string paramName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(paramName, nameof(paramName));

            if (_supportedSearchParams.TryGetValue(paramName, out SearchParam searchParam))
            {
                return searchParam;
            }

            throw new SearchParameterNotSupportedException(ResourceType, paramName);
        }
    }
}
