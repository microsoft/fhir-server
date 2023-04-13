// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Provides mechanism to access search parameter definition.
    /// </summary>
    public interface ISearchParameterDefinitionManager
    {
        public delegate ISearchParameterDefinitionManager SearchableSearchParameterDefinitionManagerResolver();

        public delegate ISearchParameterDefinitionManager SupportedSearchParameterDefinitionManagerResolver();

        /// <summary>
        /// Gets the list of all search parameters.
        /// </summary>
        IEnumerable<SearchParameterInfo> AllSearchParameters { get; }

        /// <summary>
        /// Represents a mapping of resource type to a hash of the search parameters
        /// currently supported for that resource type.
        /// </summary>
        IReadOnlyDictionary<string, string> SearchParameterHashMap { get; }

        /// <summary>
        /// Gets list of search parameters for the given <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The resource type whose list of search parameters should be returned.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the search parameters.</returns>
        IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType);

        /// <summary>
        /// Retrieves the search parameter with <paramref name="code"/> associated with <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="code">The code of the search parameter.</param>
        /// <param name="searchParameter">When this method returns, the search parameter with the given <paramref name="code"/> associated with the <paramref name="resourceType"/> if it exists; otherwise, the default value.</param>
        /// <returns><c>true</c> if the search parameter exists; otherwise, <c>false</c>.</returns>
        bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter);

        /// <summary>
        /// Retrieves the search parameter with <paramref name="code"/> associated with <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="code">The code of the search parameter.</param>
        /// <returns>The search parameter with the given <paramref name="code"/> associated with the <paramref name="resourceType"/>.</returns>
        SearchParameterInfo GetSearchParameter(string resourceType, string code);

        /// <summary>
        /// Retrieves the search parameter with <paramref name="definitionUri"/>.
        /// </summary>
        /// <param name="definitionUri">The search parameter definition URL.</param>
        /// <param name="value">The SearchParameterInfo pertaining to the specified <paramref name="definitionUri"/></param>
        /// <returns>True if the search parameter is found <paramref name="definitionUri"/>.</returns>
        public bool TryGetSearchParameter(string definitionUri, out SearchParameterInfo value);

        /// <summary>
        /// Retrieves the search parameter with <paramref name="definitionUri"/>.
        /// </summary>
        /// <param name="definitionUri">The search parameter definition URL.</param>
        /// <returns>The search parameter with the given <paramref name="definitionUri"/>.</returns>
        SearchParameterInfo GetSearchParameter(string definitionUri);

        /// <summary>
        /// Updates the existing resource type - search parameter hash mapping with the given new values.
        /// </summary>
        /// <param name="updatedSearchParamHashMap">Dictionary containing resource type to search parameter hash values</param>
        public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap);

        /// <summary>
        /// Gets the hash of the current search parameters that are supported for the given resource type.
        /// </summary>
        /// <param name="resourceType">Resource type for which we need the hash of search parameters.</param>
        /// <returns>A string representing a hash of the search parameters.</returns>
        public string GetSearchParameterHashForResourceType(string resourceType);

        /// <summary>
        /// Allows addition of a new search parameters at runtime.
        /// </summary>
        /// <param name="searchParameters">An collection containing SearchParameter resources.</param>
        /// <param name="calculateHash">Indicates whether the search parameter hash should be recalculated</param>
        void AddNewSearchParameters(IReadOnlyCollection<ITypedElement> searchParameters, bool calculateHash = true);

        /// <summary>
        /// Allows removal of a custom search parameter.
        /// </summary>
        /// <param name="searchParam">The custom search parameter to remove.</param>
        void DeleteSearchParameter(ITypedElement searchParam);

        /// <summary>
        /// Allows removal of a custom search parameter.
        /// </summary>
        /// <param name="url">The url identifying the custom search parameter to remove.</param>
        /// <param name="calculateHash">Indicated whether the search parameter hash values should be recalulated after this delete.</param>
        void DeleteSearchParameter(string url, bool calculateHash = true);

        /// <summary>
        /// Retrieves the search parameters that match the given <paramref name="resourceTypes"/>.
        /// </summary>
        /// <param name="resourceTypes">Collection of resource type names</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the search parameters.</returns>
        public IEnumerable<SearchParameterInfo> GetSearchParametersByResourceTypes(ICollection<string> resourceTypes);

        /// <summary>
        /// Retrieves the search parameters that match the given <paramref name="definitionUrls"/>.
        /// </summary>
        /// <param name="definitionUrls">Collection of definition urls</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the search parameters.</returns>
        public IEnumerable<SearchParameterInfo> GetSearchParametersByUrls(ICollection<string> definitionUrls);

        /// <summary>
        /// Retrieves the search parameters that match the given <paramref name="codes"/>.
        /// </summary>
        /// <param name="codes">Collection of codes</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the search parameters.</returns>
        public IEnumerable<SearchParameterInfo> GetSearchParametersByCodes(ICollection<string> codes);

        /// <summary>
        /// Retrieves the search parameters that match the given <paramref name="ids"/>.
        /// </summary>
        /// <param name="ids">Collection of codes</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the search parameters.</returns>
        public IEnumerable<SearchParameterInfo> GetSearchParametersByIds(ICollection<string> ids);
    }
}
