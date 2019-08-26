// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Models;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public class SearchOptionsFactory : ISearchOptionsFactory
    {
        private static readonly Regex Base64FormatRegex = new Regex("^([A-Za-z0-9+/]{4})*([A-Za-z0-9+/]{3}=|[A-Za-z0-9+/]{2}==)?$", RegexOptions.Compiled | RegexOptions.Singleline);

        private readonly IExpressionParser _expressionParser;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ILogger _logger;
        private readonly SearchParameterInfo _resourceTypeSearchParameter;

        public SearchOptionsFactory(
            IExpressionParser expressionParser,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ILogger<SearchOptionsFactory> logger)
        {
            EnsureArg.IsNotNull(expressionParser, nameof(expressionParser));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _expressionParser = expressionParser;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _logger = logger;

            _resourceTypeSearchParameter = searchParameterDefinitionManager.GetSearchParameter(ResourceType.Resource.ToString(), SearchParameterNames.ResourceType);
        }

        public SearchOptions Create(string resourceType, IReadOnlyList<Tuple<string, string>> queryParameters)
        {
            return Create(null, null, resourceType, queryParameters);
        }

        public SearchOptions Create(string compartmentType, string compartmentId, string resourceType, IReadOnlyList<Tuple<string, string>> queryParameters)
        {
            var searchOptions = new SearchOptions();

            string continuationToken = null;

            var searchParams = new SearchParams();
            var unsupportedSearchParameters = new List<Tuple<string, string>>();

            // Extract the continuation token, filter out the other known query parameters that's not search related.
            foreach (Tuple<string, string> query in queryParameters ?? Enumerable.Empty<Tuple<string, string>>())
            {
                if (query.Item1 == KnownQueryParameterNames.ContinuationToken)
                {
                    // This is an unreachable case. The mapping of the query parameters makes it so only one continuation token can exist.
                    if (continuationToken != null)
                    {
                        throw new InvalidSearchOperationException(
                            string.Format(Core.Resources.MultipleQueryParametersNotAllowed, KnownQueryParameterNames.ContinuationToken));
                    }

                    // Checks if the continuation token is base 64 bit encoded. Needed for systems that have cached continuation tokens from before they were encoded.
                    if (Base64FormatRegex.IsMatch(query.Item2))
                    {
                        continuationToken = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(query.Item2));
                    }
                    else
                    {
                        continuationToken = query.Item2;
                    }
                }
                else if (query.Item1 == KnownQueryParameterNames.Format)
                {
                    // TODO: We need to handle format parameter.
                }
                else if (string.IsNullOrWhiteSpace(query.Item1) || string.IsNullOrWhiteSpace(query.Item2))
                {
                    // Query parameter with empty value is not supported.
                    unsupportedSearchParameters.Add(query);
                }
                else
                {
                    // Parse the search parameters.
                    try
                    {
                        searchParams.Add(query.Item1, query.Item2);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation(ex, "Failed to parse the query parameter. Skipping.");

                        // There was a problem parsing the parameter. Add it to list of unsupported parameters.
                        unsupportedSearchParameters.Add(query);
                    }
                }
            }

            searchOptions.ContinuationToken = continuationToken;

            // Check the item count.
            if (searchParams.Count != null)
            {
                searchOptions.MaxItemCount = searchParams.Count.Value;
            }

            // Check to see if only the count should be returned
            searchOptions.CountOnly = searchParams.Summary == SummaryType.Count;

            // If the resource type is not specified, then the common
            // search parameters should be used.
            ResourceType parsedResourceType = ResourceType.DomainResource;

            if (!string.IsNullOrWhiteSpace(resourceType) &&
                !Enum.TryParse(resourceType, out parsedResourceType))
            {
                throw new ResourceNotSupportedException(resourceType);
            }

            var searchExpressions = new List<Expression>();

            if (!string.IsNullOrWhiteSpace(resourceType))
            {
                searchExpressions.Add(Expression.SearchParameter(_resourceTypeSearchParameter, Expression.StringEquals(FieldName.TokenCode, null, resourceType, false)));
            }

            searchExpressions.AddRange(searchParams.Parameters.Select(
                    q =>
                    {
                        try
                        {
                            return _expressionParser.Parse(parsedResourceType.ToString(), q.Item1, q.Item2);
                        }
                        catch (SearchParameterNotSupportedException)
                        {
                            unsupportedSearchParameters.Add(q);

                            return null;
                        }
                    })
                .Where(item => item != null));

            if (!string.IsNullOrWhiteSpace(compartmentType))
            {
                if (Enum.TryParse(compartmentType, out CompartmentType parsedCompartmentType))
                {
                    if (string.IsNullOrWhiteSpace(compartmentId))
                    {
                        throw new InvalidSearchOperationException(Core.Resources.CompartmentIdIsInvalid);
                    }

                    searchExpressions.Add(Expression.CompartmentSearch(compartmentType, compartmentId));
                }
                else
                {
                    throw new InvalidSearchOperationException(string.Format(Core.Resources.CompartmentTypeIsInvalid, compartmentType));
                }
            }

            if (searchExpressions.Count == 1)
            {
                searchOptions.Expression = searchExpressions[0];
            }
            else if (searchExpressions.Count > 1)
            {
                searchOptions.Expression = Expression.And(searchExpressions.ToArray());
            }

            if (unsupportedSearchParameters.Any())
            {
                // TODO: Client can specify whether exception should be raised or not when it encounters unknown search parameters.
                // For now, we will ignore any unknown search parameters.
            }

            searchOptions.UnsupportedSearchParams = unsupportedSearchParameters;

            if (searchParams.Sort?.Count > 0)
            {
                var sortings = new List<(SearchParameterInfo, SortOrder)>();
                List<(string parameterName, string reason)> unsupportedSortings = null;

                foreach (Tuple<string, Hl7.Fhir.Rest.SortOrder> sorting in searchParams.Sort)
                {
                    try
                    {
                        SearchParameterInfo searchParameterInfo = _searchParameterDefinitionManager.GetSearchParameter(parsedResourceType.ToString(), sorting.Item1);
                        sortings.Add((searchParameterInfo, sorting.Item2.ToCoreSortOrder()));
                    }
                    catch (SearchParameterNotSupportedException)
                    {
                        (unsupportedSortings ?? (unsupportedSortings = new List<(string parameterName, string reason)>())).Add((sorting.Item1, string.Format(Core.Resources.SearchParameterNotSupported, sorting.Item1, resourceType)));
                    }
                }

                searchOptions.Sort = sortings;
                searchOptions.UnsupportedSortingParams = (IReadOnlyList<(string parameterName, string reason)>)unsupportedSortings ?? Array.Empty<(string parameterName, string reason)>();
            }
            else
            {
                searchOptions.Sort = Array.Empty<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>();
                searchOptions.UnsupportedSortingParams = Array.Empty<(string parameterName, string reason)>();
            }

            return searchOptions;
        }
    }
}
