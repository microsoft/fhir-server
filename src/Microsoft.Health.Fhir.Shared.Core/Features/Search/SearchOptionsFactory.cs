// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Models;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public class SearchOptionsFactory : ISearchOptionsFactory
    {
        private static readonly string SupportedTotalTypes = $"'{TotalType.Accurate}', '{TotalType.None}'".ToLower(CultureInfo.CurrentCulture);

        private static readonly List<string> IncludeIterateModifiers = new List<string> { "_include:iterate", "_include:recurse" };
        private static readonly List<string> RevIncludeIterateModifiers = new List<string> { "_revinclude:iterate", "_revinclude:recurse" };
        private static readonly List<string> AllIterateModifiers = IncludeIterateModifiers.Concat(RevIncludeIterateModifiers).ToList();

        private readonly IExpressionParser _expressionParser;
        private readonly IFhirRequestContextAccessor _contextAccessor;
        private readonly ISortingValidator _sortingValidator;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ILogger _logger;
        private readonly SearchParameterInfo _resourceTypeSearchParameter;
        private readonly CoreFeatureConfiguration _featureConfiguration;

        public SearchOptionsFactory(
            IExpressionParser expressionParser,
            ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver searchParameterDefinitionManagerResolver,
            IOptions<CoreFeatureConfiguration> featureConfiguration,
            IFhirRequestContextAccessor contextAccessor,
            ISortingValidator sortingValidator,
            ILogger<SearchOptionsFactory> logger)
        {
            EnsureArg.IsNotNull(expressionParser, nameof(expressionParser));
            EnsureArg.IsNotNull(searchParameterDefinitionManagerResolver, nameof(searchParameterDefinitionManagerResolver));
            EnsureArg.IsNotNull(featureConfiguration?.Value, nameof(featureConfiguration));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(sortingValidator, nameof(sortingValidator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _expressionParser = expressionParser;
            _contextAccessor = contextAccessor;
            _sortingValidator = sortingValidator;
            _searchParameterDefinitionManager = searchParameterDefinitionManagerResolver();
            _logger = logger;
            _featureConfiguration = featureConfiguration.Value;

            _resourceTypeSearchParameter = _searchParameterDefinitionManager.GetSearchParameter(ResourceType.Resource.ToString(), SearchParameterNames.ResourceType);
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
            bool setDefaultBundleTotal = true;

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

                    try
                    {
                        continuationToken = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(query.Item2));
                    }
                    catch (FormatException)
                    {
                        throw new BadRequestException(Core.Resources.InvalidContinuationToken);
                    }

                    setDefaultBundleTotal = false;
                }
                else if (query.Item1 == KnownQueryParameterNames.Format)
                {
                    // TODO: We need to handle format parameter.
                }
                else if (string.Equals(query.Item1, KnownQueryParameterNames.Type, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(query.Item2))
                    {
                        throw new BadRequestException(string.Format(Core.Resources.InvalidTypeParameter, query.Item2));
                    }

                    var types = query.Item2.SplitByOrSeparator();
                    var badTypes = types.Where(type => !ModelInfoProvider.IsKnownResource(type)).ToHashSet();

                    if (badTypes.Count != 0)
                    {
                        _contextAccessor.FhirRequestContext?.BundleIssues.Add(
                            new OperationOutcomeIssue(
                                OperationOutcomeConstants.IssueSeverity.Warning,
                                OperationOutcomeConstants.IssueType.NotSupported,
                                string.Format(Core.Resources.InvalidTypeParameter, badTypes.OrderBy(x => x).Select(type => $"'{type}'").JoinByOrSeparator())));
                        if (badTypes.Count != types.Count)
                        {
                            // In case of we have acceptable types, we filter invalid types from search.
                            searchParams.Add(KnownQueryParameterNames.Type, types.Except(badTypes).JoinByOrSeparator());
                        }
                        else
                        {
                            // If all types are invalid, we add them to search params. If we remove them, we wouldn't filter by type, and return all types,
                            // which is incorrect behaviour. Optimally we should indicate in search options what it would yield nothing, and skip search,
                            // but there is no option for that right now.
                            searchParams.Add(KnownQueryParameterNames.Type, query.Item2);
                        }
                    }
                    else
                    {
                        searchParams.Add(KnownQueryParameterNames.Type, query.Item2);
                    }
                }
                else if (string.IsNullOrWhiteSpace(query.Item1) || string.IsNullOrWhiteSpace(query.Item2))
                {
                    // Query parameter with empty value is not supported.
                    unsupportedSearchParameters.Add(query);
                }
                else if (string.Compare(query.Item1, KnownQueryParameterNames.Total, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (Enum.TryParse<TotalType>(query.Item2, true, out var totalType))
                    {
                        ValidateTotalType(totalType);

                        searchOptions.IncludeTotal = totalType;
                        setDefaultBundleTotal = false;
                    }
                    else
                    {
                        throw new BadRequestException(string.Format(Core.Resources.InvalidTotalParameter, query.Item2, SupportedTotalTypes));
                    }
                }
                else
                {
                    // Parse the search parameters.
                    try
                    {
                        // Basic format checking (e.g. integer value for _count key etc.).
                        searchParams.Add(query.Item1, query.Item2);
                    }
                    catch (Exception ex)
                    {
                        throw new BadRequestException(ex.Message);
                    }
                }
            }

            searchOptions.ContinuationToken = continuationToken;

            if (setDefaultBundleTotal)
            {
                ValidateTotalType(_featureConfiguration.IncludeTotalInBundle);
                searchOptions.IncludeTotal = _featureConfiguration.IncludeTotalInBundle;
            }

            // Check the item count.
            if (searchParams.Count != null)
            {
                searchOptions.MaxItemCountSpecifiedByClient = true;

                if (searchParams.Count > _featureConfiguration.MaxItemCountPerSearch)
                {
                    searchOptions.MaxItemCount = _featureConfiguration.MaxItemCountPerSearch;

                    _contextAccessor.FhirRequestContext?.BundleIssues.Add(
                        new OperationOutcomeIssue(
                            OperationOutcomeConstants.IssueSeverity.Information,
                            OperationOutcomeConstants.IssueType.Informational,
                            string.Format(Core.Resources.SearchParamaterCountExceedLimit, _featureConfiguration.MaxItemCountPerSearch, searchParams.Count)));
                }
                else
                {
                    searchOptions.MaxItemCount = searchParams.Count.Value;
                }
            }
            else
            {
                searchOptions.MaxItemCount = _featureConfiguration.DefaultItemCountPerSearch;
            }

            searchOptions.IncludeCount = _featureConfiguration.DefaultIncludeCountPerSearch;

            if (searchParams.Elements?.Any() == true && searchParams.Summary != null && searchParams.Summary != SummaryType.False)
            {
                // The search parameters _elements and _summarize cannot be specified for the same request.
                throw new BadRequestException(string.Format(Core.Resources.ElementsAndSummaryParametersAreIncompatible, KnownQueryParameterNames.Summary, KnownQueryParameterNames.Elements));
            }

            // Check to see if only the count should be returned
            searchOptions.CountOnly = searchParams.Summary == SummaryType.Count;

            // If the resource type is not specified, then the common
            // search parameters should be used.
            ResourceType[] parsedResourceTypes = new[] { ResourceType.DomainResource };

            var searchExpressions = new List<Expression>();
            if (string.IsNullOrWhiteSpace(resourceType))
            {
                // Try to parse resource types from _type Search Parameter
                // This will result in empty array if _type has any modifiers
                // Which is good, since :not modifier changes the meaning of the
                // search parameter and we can no longer use it to deduce types
                // (and should proceed with ResourceType.DomainResource in that case)
                var resourceTypes = searchParams.Parameters
                    .Where(q => q.Item1 == KnownQueryParameterNames.Type) // <-- Equality comparison to avoid modifiers
                    .SelectMany(q => q.Item2.SplitByOrSeparator())
                    .Where(type => ModelInfoProvider.IsKnownResource(type))
                    .Select(x =>
                    {
                        if (!Enum.TryParse(x, out ResourceType parsedType))
                        {
                            // Should never get here
                            throw new ResourceNotSupportedException(x);
                        }

                        return parsedType;
                    })
                    .Distinct();

                if (resourceTypes.Any())
                {
                    parsedResourceTypes = resourceTypes.ToArray();
                }
            }
            else
            {
                if (!Enum.TryParse(resourceType, out parsedResourceTypes[0]))
                {
                    throw new ResourceNotSupportedException(resourceType);
                }

                searchExpressions.Add(Expression.SearchParameter(_resourceTypeSearchParameter, Expression.StringEquals(FieldName.TokenCode, null, resourceType, false)));
            }

            var resourceTypesString = parsedResourceTypes.Select(x => x.ToString()).ToArray();
            searchExpressions.AddRange(searchParams.Parameters.Select(
                    q =>
                    {
                        try
                        {
                            return _expressionParser.Parse(resourceTypesString, q.Item1, q.Item2);
                        }
                        catch (SearchParameterNotSupportedException)
                        {
                            unsupportedSearchParameters.Add(q);

                            return null;
                        }
                    })
                .Where(item => item != null));

            if (searchParams.Include?.Count > 0)
            {
                searchExpressions.AddRange(searchParams.Include.Select(
                    q => _expressionParser.ParseInclude(resourceTypesString, q, false /* not reversed */, false /* no iterate */))
                    .Where(item => item != null));
            }

            if (searchParams.RevInclude?.Count > 0)
            {
                searchExpressions.AddRange(searchParams.RevInclude.Select(
                    q => _expressionParser.ParseInclude(resourceTypesString, q, true /* reversed */, false /* no iterate */))
                    .Where(item => item != null));
            }

            // Parse _include:iterate (_include:recurse) parameters.
            // :iterate (:recurse) modifiers are not supported by Hl7.Fhir.Rest, hence not added to the Include collection and exist in the Parameters list.
            // See https://github.com/FirelyTeam/fhir-net-api/issues/222
            // _include:iterate (_include:recurse) expression may appear without a preceding _include parameter
            // when applied on a circular reference
            searchExpressions.AddRange(ParseIncludeIterateExpressions(searchParams));

            // remove _include:iterate and _revinclude:iterate parameters from unsupportedSearchParameters
            unsupportedSearchParameters.RemoveAll(p => AllIterateModifiers.Contains(p.Item1));

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
                var sortings = new List<(SearchParameterInfo, SortOrder)>(searchParams.Sort.Count);
                bool sortingsValid = true;

                foreach (Tuple<string, Hl7.Fhir.Rest.SortOrder> sorting in searchParams.Sort)
                {
                    try
                    {
                        SearchParameterInfo searchParameterInfo = resourceTypesString.Select(t => _searchParameterDefinitionManager.GetSearchParameter(t, sorting.Item1)).Distinct().First();
                        sortings.Add((searchParameterInfo, sorting.Item2.ToCoreSortOrder()));
                    }
                    catch (SearchParameterNotSupportedException)
                    {
                        sortingsValid = false;
                        _contextAccessor.FhirRequestContext?.BundleIssues.Add(new OperationOutcomeIssue(
                            OperationOutcomeConstants.IssueSeverity.Warning,
                            OperationOutcomeConstants.IssueType.NotSupported,
                            string.Format(CultureInfo.InvariantCulture, Core.Resources.SearchParameterNotSupported, sorting.Item1, string.Join(", ", resourceTypesString))));
                    }
                }

                if (sortingsValid)
                {
                    if (!_sortingValidator.ValidateSorting(sortings, out IReadOnlyList<string> errorMessages))
                    {
                        if (errorMessages == null || errorMessages.Count == 0)
                        {
                            throw new InvalidOperationException($"Expected {_sortingValidator.GetType().Name} to return error messages when {nameof(_sortingValidator.ValidateSorting)} returns false");
                        }

                        sortingsValid = false;

                        foreach (var errorMessage in errorMessages)
                        {
                            _contextAccessor.FhirRequestContext?.BundleIssues.Add(new OperationOutcomeIssue(
                                OperationOutcomeConstants.IssueSeverity.Warning,
                                OperationOutcomeConstants.IssueType.NotSupported,
                                errorMessage));
                        }
                    }
                }

                if (sortingsValid)
                {
                    searchOptions.Sort = sortings;
                }
            }

            if (searchOptions.Sort == null)
            {
                searchOptions.Sort = Array.Empty<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>();
            }

            return searchOptions;

            IEnumerable<IncludeExpression> ParseIncludeIterateExpressions(SearchParams searchParams)
            {
                return searchParams.Parameters
                .Where(p => p != null && AllIterateModifiers.Where(m => string.Equals(p.Item1, m, StringComparison.OrdinalIgnoreCase)).Any())
                .Select(p =>
                {
                    var includeResourceType = p.Item2?.Split(':')[0];
                    if (!ModelInfoProvider.IsKnownResource(includeResourceType))
                    {
                        throw new ResourceNotSupportedException(includeResourceType);
                    }

                    var reversed = RevIncludeIterateModifiers.Contains(p.Item1);
                    var expression = _expressionParser.ParseInclude(new[] { includeResourceType }, p.Item2, reversed, true);

                    // Reversed Iterate expressions (not wildcard) must specify target type if there is more than one possible target type
                    if (expression.Reversed && expression.Iterate && expression.TargetResourceType == null && expression.ReferenceSearchParameter?.TargetResourceTypes?.Count > 1)
                    {
                        throw new BadRequestException(string.Format(Core.Resources.RevIncludeIterateTargetTypeNotSpecified, p.Item2));
                    }

                    // For circular include iterate expressions, add an informational issue indicating that a single iteration is supported.
                    // See https://www.hl7.org/fhir/search.html#revinclude.
                    if (expression.Iterate && expression.CircularReference)
                    {
                        _contextAccessor.FhirRequestContext?.BundleIssues.Add(
                        new OperationOutcomeIssue(
                            OperationOutcomeConstants.IssueSeverity.Information,
                            OperationOutcomeConstants.IssueType.Informational,
                            string.Format(Core.Resources.IncludeIterateCircularReferenceExecutedOnce, p.Item1, p.Item2)));
                    }

                    return expression;
                });
            }

            void ValidateTotalType(TotalType totalType)
            {
                // Estimate is not yet supported.
                if (totalType == TotalType.Estimate)
                {
                    throw new SearchOperationNotSupportedException(string.Format(Core.Resources.UnsupportedTotalParameter, totalType, SupportedTotalTypes));
                }
            }
        }
    }
}
