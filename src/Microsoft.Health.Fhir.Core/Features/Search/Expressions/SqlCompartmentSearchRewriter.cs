// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Rewrites SqlCompartmentSearchRewriter to use main search index.
    /// </summary>
    public class SqlCompartmentSearchRewriter : CompartmentSearchRewriter
    {
        private const string ClinicalPatientSearchParameterUrl = "http://hl7.org/fhir/SearchParameter/clinical-patient";

        // Compartment-definition search parameters whose FHIRPath expression filters a polymorphic reference with
        // resolve() (e.g. "Encounter.participant.individual.where(resolve() is Practitioner)"). resolve() cannot be
        // evaluated during indexing, so these parameters are never materialized as ReferenceSearchParam rows and a
        // membership predicate keyed on them matches nothing. Each entry maps the unmaterialized parameter URL to
        // the codes of materialized parameters that index the SAME elements; the equivalent is validated at
        // resolution time (reference type, supported, targets the compartment type) and the membership predicate
        // additionally constrains the reference to the compartment root's type and id, so an equivalent that
        // indexes a broader element set cannot widen membership beyond the formal definition.
        // Known gap: EpisodeOfCare-care-manager (Practitioner compartment) is resolve()-based and has NO
        // materialized equivalent, so EpisodeOfCare membership in the Practitioner compartment cannot be
        // enumerated until the parameter is indexable; the formal parameter is retained (matches nothing).
        private static readonly Dictionary<string, IReadOnlyCollection<string>> MaterializedEquivalentSearchParameterCodes =
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
            {
                // Patient compartment.
                [ClinicalPatientSearchParameterUrl] = new[] { "patient", "subject" },
                ["http://hl7.org/fhir/SearchParameter/AuditEvent-patient"] = new[] { "agent", "entity" },
                ["http://hl7.org/fhir/SearchParameter/Basic-patient"] = new[] { "subject" },
                ["http://hl7.org/fhir/SearchParameter/Invoice-patient"] = new[] { "subject" },
                ["http://hl7.org/fhir/SearchParameter/MeasureReport-patient"] = new[] { "subject" },
                ["http://hl7.org/fhir/SearchParameter/Person-patient"] = new[] { "link" },
                ["http://hl7.org/fhir/SearchParameter/Provenance-patient"] = new[] { "target" },

                // Practitioner compartment.
                ["http://hl7.org/fhir/SearchParameter/Encounter-practitioner"] = new[] { "participant" },
                ["http://hl7.org/fhir/SearchParameter/Person-practitioner"] = new[] { "link" },
            };

        public SqlCompartmentSearchRewriter(
            Lazy<ICompartmentDefinitionManager> compartmentDefinitionManager,
            Lazy<ISearchParameterDefinitionManager> searchParameterDefinitionManager)
            : base(compartmentDefinitionManager, searchParameterDefinitionManager)
        {
        }

        public override List<Expression> BuildCompartmentSearchExpressionsGroup(CompartmentSearchExpression expression)
        {
            SearchParameterInfo resourceTypeSearchParameter = SearchParameterDefinitionManager.Value.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType);

            var compartmentType = expression.CompartmentType;
            var compartmentId = expression.CompartmentId;

            if (Enum.TryParse(compartmentType, out ValueSets.CompartmentType parsedCompartmentType))
            {
                if (string.IsNullOrWhiteSpace(compartmentId))
                {
                    throw new InvalidSearchOperationException(Core.Resources.CompartmentIdIsInvalid);
                }

                var searchParameterInfoList = new Dictionary<string, (SearchParameterInfo searchParameterInfo, HashSet<string> ResourceTypes)>();

                IReadOnlyDictionary<string, IReadOnlyCollection<SearchParameterInfo>> materializedParameters =
                    GetMaterializedCompartmentSearchParameters(
                        compartmentType,
                        expression.FilteredResourceTypes,
                        includeMaterializedEquivalents: expression is SmartCompartmentSearchExpression);

                foreach ((string compartmentResourceType, IReadOnlyCollection<SearchParameterInfo> searchParameters) in materializedParameters)
                {
                    foreach (SearchParameterInfo searchParameter in searchParameters)
                    {
                        string searchParamUrl = searchParameter.Url.ToString();
                        if (searchParameterInfoList.TryGetValue(searchParamUrl, out var existing))
                        {
                            existing.ResourceTypes.Add(compartmentResourceType);
                        }
                        else
                        {
                            searchParameterInfoList[searchParamUrl] = (searchParameter, new HashSet<string> { compartmentResourceType });
                        }
                    }
                }

                var searchParamAndResourceTypeExpressions = new List<Expression>();
                var finalCompartmentSearchExpressions = new List<Expression>();

                if (searchParameterInfoList.Any())
                {
                    foreach (var grouping in searchParameterInfoList)
                    {
                        // Always add the applicable resource types
                        Expression innerExpression = grouping.Value.ResourceTypes.Count > 1 ? Expression.In(FieldName.TokenCode, null, grouping.Value.ResourceTypes) : Expression.StringEquals(FieldName.TokenCode, null, grouping.Value.ResourceTypes.FirstOrDefault(), false);
                        SearchParameterExpression resourceTypesExpression = Expression.SearchParameter(
                            resourceTypeSearchParameter,
                            innerExpression);

                        searchParamAndResourceTypeExpressions.Add(Expression.SearchParameter(searchParameterInfoList[grouping.Key].searchParameterInfo, resourceTypesExpression));
                    }

                    if (searchParamAndResourceTypeExpressions.Any())
                    {
                        // Get the ORed expression of search parameter + resource type expressions
                        // Then AND with the compartment type and id to ensure we only get resources in the compartment
                        var oredExpression = Expression.Or(searchParamAndResourceTypeExpressions);
                        finalCompartmentSearchExpressions.Add(Expression.And(
                                oredExpression,
                                Expression.StringEquals(FieldName.ReferenceResourceType, null, compartmentType, false),
                                Expression.StringEquals(FieldName.ReferenceResourceId, null, compartmentId, false)));
                    }
                }
                else
                {
                    finalCompartmentSearchExpressions.Add(expression);
                }

                return finalCompartmentSearchExpressions;
            }
            else
            {
                throw new InvalidSearchOperationException(string.Format(Core.Resources.CompartmentTypeIsInvalid, compartmentType));
            }
        }

        /// <summary>
        /// Gets the supported, materialized reference parameters that formally establish compartment membership.
        /// </summary>
        /// <param name="compartmentType">The compartment resource type.</param>
        /// <param name="filteredResourceTypes">Optional resource types to include.</param>
        /// <param name="includeMaterializedEquivalents">Whether unmaterialized combined parameters should resolve to validated materialized equivalents.</param>
        /// <returns>Materialized membership parameters grouped by resource type.</returns>
        public IReadOnlyDictionary<string, IReadOnlyCollection<SearchParameterInfo>> GetMaterializedCompartmentSearchParameters(
            string compartmentType,
            IEnumerable<string> filteredResourceTypes,
            bool includeMaterializedEquivalents)
        {
            if (!Enum.TryParse(compartmentType, out ValueSets.CompartmentType parsedCompartmentType))
            {
                throw new InvalidSearchOperationException(string.Format(Core.Resources.CompartmentTypeIsInvalid, compartmentType));
            }

            if (!CompartmentDefinitionManager.Value.TryGetResourceTypes(parsedCompartmentType, out HashSet<string> resourceTypes))
            {
                return new Dictionary<string, IReadOnlyCollection<SearchParameterInfo>>();
            }

            HashSet<string> filters = filteredResourceTypes?.ToHashSet(StringComparer.Ordinal);
            bool filterByResourceType = filters?.Any(resourceType => !string.Equals(resourceType, KnownResourceTypes.DomainResource, StringComparison.Ordinal)) == true;
            var result = new Dictionary<string, IReadOnlyCollection<SearchParameterInfo>>(StringComparer.Ordinal);

            foreach (string resourceType in resourceTypes.Where(resourceType => !filterByResourceType || filters.Contains(resourceType)))
            {
                if (!CompartmentDefinitionManager.Value.TryGetSearchParams(resourceType, parsedCompartmentType, out HashSet<string> parameterCodes))
                {
                    continue;
                }

                var parameters = new Dictionary<string, SearchParameterInfo>(StringComparer.Ordinal);
                foreach (string parameterCode in parameterCodes)
                {
                    if (!SearchParameterDefinitionManager.Value.TryGetSearchParameter(resourceType, parameterCode, out SearchParameterInfo parameter))
                    {
                        continue;
                    }

                    if (includeMaterializedEquivalents
                        && MaterializedEquivalentSearchParameterCodes.TryGetValue(parameter.Url.AbsoluteUri, out IReadOnlyCollection<string> equivalentCodes))
                    {
                        bool equivalentFound = false;
                        foreach (string equivalentCode in equivalentCodes)
                        {
                            if (SearchParameterDefinitionManager.Value.TryGetSearchParameter(resourceType, equivalentCode, out SearchParameterInfo equivalent)
                                && !string.Equals(equivalent.Url.AbsoluteUri, parameter.Url.AbsoluteUri, StringComparison.Ordinal)
                                && equivalent.Type == SearchParamType.Reference
                                && equivalent.IsSupported
                                && equivalent.TargetResourceTypes?.Contains(compartmentType, StringComparer.Ordinal) == true)
                            {
                                parameters[equivalent.Url.AbsoluteUri] = equivalent;
                                equivalentFound = true;
                            }
                        }

                        // Some resources use a directly indexable branch of the combined parameter and have no
                        // resource-specific equivalent. Retain the formal parameter for those resources.
                        if (!equivalentFound && parameter.Type == SearchParamType.Reference && parameter.IsSupported)
                        {
                            parameters[parameter.Url.AbsoluteUri] = parameter;
                        }
                    }
                    else if (parameter.Type == SearchParamType.Reference && parameter.IsSupported)
                    {
                        parameters[parameter.Url.AbsoluteUri] = parameter;
                    }
                }

                if (parameters.Count > 0)
                {
                    result[resourceType] = parameters.Values.ToArray();
                }
            }

            return result;
        }
    }
}
