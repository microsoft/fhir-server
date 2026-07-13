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
        /// <summary>
        /// The materialized reference parameter that carries Patient-compartment membership for resource types whose
        /// FHIR CompartmentDefinition nominates the non-materialized combined <c>patient</c> parameter. For the Patient
        /// compartment this is always the type's <c>subject</c> parameter (e.g. Encounter.subject, ImagingStudy.subject).
        /// </summary>
        private const string MaterializedCompartmentCarrierCode = "subject";

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

                var compartmentResourceTypesToSearch = new HashSet<string>();
                var searchParameterInfoList = new Dictionary<string, (SearchParameterInfo searchParameterInfo, HashSet<string> ResourceTypes)>();

                void AddCompartmentSearchParameter(SearchParameterInfo searchParameter, string resourceType)
                {
                    // Use the URL string as the key.
                    string searchParamUrl = searchParameter.Url.ToString();

                    if (searchParameterInfoList.TryGetValue(searchParamUrl, out var existing))
                    {
                        // Add the compartment resource type if the key exists.
                        existing.ResourceTypes.Add(resourceType);
                    }
                    else
                    {
                        // Otherwise, add a new dictionary entry.
                        searchParameterInfoList[searchParamUrl] = (searchParameter, new HashSet<string> { resourceType });
                    }
                }

                // A SMART compartment is broader than a regular compartment search: the SMART user has access to any
                // resource that references them (see SmartCompartmentSearchRewriter). Membership is still defined by the
                // FHIR CompartmentDefinition -- only the parameters it nominates confer compartment membership. We must
                // NOT admit a resource merely because it carries some reference to the compartment root through an
                // unrelated parameter (e.g. Observation.focus), otherwise a SMART caller could read another patient's
                // data (MSRC over-match). The one adjustment made below is a narrow, security-reviewed substitution of
                // the non-materialized combined `patient` parameter with the resource type's materialized `subject`
                // carrier, which is the very element the Patient compartment is defined on for those types.
                bool isSmartCompartment = expression is SmartCompartmentSearchExpression;

                if (CompartmentDefinitionManager.Value.TryGetResourceTypes(parsedCompartmentType, out HashSet<string> resourceTypes))
                {
                    if (expression.FilteredResourceTypes.Any(resourceType => !string.Equals(resourceType, KnownResourceTypes.DomainResource, StringComparison.Ordinal)))
                    {
                        resourceTypes = resourceTypes.Where(x => expression.FilteredResourceTypes.Contains(x)).ToHashSet();
                    }

                    foreach (var resourceFilter in resourceTypes)
                    {
                        compartmentResourceTypesToSearch.Add(resourceFilter);
                    }
                }

                foreach (var compartmentResourceType in compartmentResourceTypesToSearch)
                {
                    if (CompartmentDefinitionManager.Value.TryGetSearchParams(compartmentResourceType, parsedCompartmentType, out HashSet<string> compartmentSearchParameters))
                    {
                        foreach (var compartmentSearchParameter in compartmentSearchParameters)
                        {
                            if (SearchParameterDefinitionManager.Value.TryGetSearchParameter(compartmentResourceType, compartmentSearchParameter, out SearchParameterInfo sp))
                            {
                                AddCompartmentSearchParameter(sp, compartmentResourceType);

                                // SMART-only substitution for the non-materialized combined `patient` compartment
                                // parameter. The FHIR Patient CompartmentDefinition nominates the combined `patient`
                                // parameter (code "patient" -> clinical-patient) for the resource types that reference
                                // the patient through their `subject` element (Encounter, ImagingStudy, Procedure,
                                // Condition, ...). clinical-patient's FHIRPath uses resolve(), so this server never
                                // materializes it as a ReferenceSearchParam row; a membership predicate keyed on it
                                // matches nothing and would silently drop those in-compartment resources. We therefore
                                // also add the resource type's materialized `subject` parameter, which indexes exactly
                                // that reference and is precisely the element the Patient compartment is defined on for
                                // these types.
                                //
                                // This mapping is intentionally narrow -- `patient` -> `subject` only. It never
                                // introduces an unrelated reference such as Observation.focus (the MSRC over-match), so
                                // it cannot broaden the caller's compartment beyond the FHIR compartment definition.
                                // Resource types whose patient reference is carried only by the non-materialized
                                // `patient` element (e.g. Immunization) have no materialized `subject` carrier and
                                // remain a known gap (ADO #197858).
                                if (isSmartCompartment
                                    && IsNonMaterializedCompartmentParameter(sp)
                                    && SearchParameterDefinitionManager.Value.TryGetSearchParameter(compartmentResourceType, MaterializedCompartmentCarrierCode, out SearchParameterInfo carrierParameter)
                                    && carrierParameter.Type == SearchParamType.Reference
                                    && carrierParameter.IsSupported
                                    && carrierParameter.TargetResourceTypes != null
                                    && carrierParameter.TargetResourceTypes.Contains(compartmentType, StringComparer.Ordinal))
                                {
                                    AddCompartmentSearchParameter(carrierParameter, compartmentResourceType);
                                }
                            }
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
        /// Returns <c>true</c> when the compartment parameter is one of the combined <c>clinical-*</c> parameters (such
        /// as <c>clinical-patient</c>) whose FHIRPath uses <c>resolve()</c>. The server does not persist a
        /// <c>ReferenceSearchParam</c> row for those parameters, so a compartment membership predicate keyed on them
        /// matches nothing. The SMART path uses this to decide when it must substitute the materialized
        /// <see cref="MaterializedCompartmentCarrierCode"/> carrier, while leaving already-materialized nominations
        /// (e.g. Observation.subject / Observation.performer, Coverage.beneficiary) untouched.
        /// </summary>
        private static bool IsNonMaterializedCompartmentParameter(SearchParameterInfo searchParameter)
        {
            return !string.IsNullOrEmpty(searchParameter.Expression)
                && searchParameter.Expression.Contains("resolve()", StringComparison.OrdinalIgnoreCase);
        }
    }
}
