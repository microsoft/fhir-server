// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Search.Filters
{
    /// <summary>
    /// Following US Core requirements, <see cref="MissingDataFilterCriteria"/> filters out resources with missing status codes.
    /// Reference: http://hl7.org/fhir/us/core/STU3.1.1/general-guidance.html#missing-data
    /// </summary>
    public sealed class MissingDataFilterCriteria : IFilterCriteria
    {
        private static readonly Dictionary<string, string> _requiredStatusElementsByResourceType = new()
        {
            { "AllergyIntolerance", "clinicalStatus" },
            { "Condition", "clinicalStatus" },
            { "DocumentReference", "status" },
            { "Immunization", "status" },
            { "Goal", "lifecycleStatus" },
        };

        private readonly bool _isCriteriaEnabled;

        private readonly bool _isSmartRequest;

        public MissingDataFilterCriteria(IOptions<ImplementationGuidesConfiguration> implementationGuides, RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
        {
            _isCriteriaEnabled = implementationGuides?.Value?.USCore?.MissingData ?? false;
            _isSmartRequest = fhirRequestContextAccessor?.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl ?? false;
        }

        private MissingDataFilterCriteria(bool isCriteriaEnabled, bool isSmartRequest)
        {
            _isCriteriaEnabled = isCriteriaEnabled;
            _isSmartRequest = isSmartRequest;
        }

        public static MissingDataFilterCriteria Default => new(isCriteriaEnabled: false, isSmartRequest: false);

        public SearchResult Apply(SearchResult searchResult)
        {
            EnsureArg.IsNotNull(searchResult);

            if (!_isCriteriaEnabled || !_isSmartRequest || !searchResult.Results.Any())
            {
                return searchResult;
            }

            var finalResults = new List<SearchResultEntry>();
            var searchIssues = new List<OperationOutcomeIssue>();

            foreach (SearchResultEntry resultEntry in searchResult.Results)
            {
                FilterCriteriaOutcome filteringOutcome = Match(resultEntry.Resource);

                if (filteringOutcome.Match)
                {
                    // Resource matches the criteria.
                    finalResults.Add(resultEntry);
                }
                else
                {
                    searchIssues.Add(filteringOutcome.OutcomeIssue);
                }
            }

            return new SearchResult(
                results: finalResults,
                searchResult.ContinuationToken,
                searchResult.SortOrder,
                searchResult.UnsupportedSearchParameters,
                searchIssues: searchIssues);
        }

        public FilterCriteriaOutcome Match(ResourceWrapper resourceWrapper)
        {
            if (!_isCriteriaEnabled || !_isSmartRequest)
            {
                return FilterCriteriaOutcome.MatchingOutcome;
            }

            EnsureArg.IsNotNull(resourceWrapper);

            if (IsRecordFromEligibleResourceType(resourceWrapper.ResourceTypeName, out string requiredStatusElementName))
            {
                try
                {
                    if (!ContainStatusElement(resourceWrapper, requiredStatusElementName))
                    {
                        // Resource does not contain the required status code.
                        return new FilterCriteriaOutcome(
                            match: false,
                            outcomeIssue: new OperationOutcomeIssue(
                                OperationOutcomeConstants.IssueSeverity.Error,
                                OperationOutcomeConstants.IssueType.NotFound,
                                string.Format(
                                    Core.Resources.USCoreMissingDataRequirement,
                                    resourceWrapper.ResourceTypeName,
                                    resourceWrapper.ResourceId)));
                    }
                }
                catch (JsonReaderException jsonE)
                {
                    throw new FilterCriteriaException(
                        string.Format(Core.Resources.USCoreDeserializationError, resourceWrapper.ResourceTypeName, resourceWrapper.ResourceId, jsonE.Message),
                        jsonE);
                }
                catch (XmlException xmlE)
                {
                    throw new FilterCriteriaException(
                        string.Format(Core.Resources.USCoreDeserializationError, resourceWrapper.ResourceTypeName, resourceWrapper.ResourceId, xmlE.Message),
                        xmlE);
                }
            }

            return FilterCriteriaOutcome.MatchingOutcome;
        }

        private static bool IsRecordFromEligibleResourceType(string resourceTypeName, out string requiredStatusElementName) =>
            _requiredStatusElementsByResourceType.TryGetValue(resourceTypeName, out requiredStatusElementName);

        private static bool ContainStatusElement(ResourceWrapper resourceWrapper, string requiredStatusElementName)
        {
            EnsureArg.IsNotNull(resourceWrapper);
            EnsureArg.IsNotEmptyOrWhiteSpace(requiredStatusElementName);

            if (resourceWrapper.RawResource.Format == Models.FhirResourceFormat.Json)
            {
                return ContainsJsonStatusElement(resourceWrapper, requiredStatusElementName);
            }
            else if (resourceWrapper.RawResource.Format == FhirResourceFormat.Xml)
            {
                return ContainsXmlStatusElement(resourceWrapper, requiredStatusElementName);
            }
            else
            {
                throw new ArgumentException($"Format '{resourceWrapper.RawResource.Format}' is a not supported format");
            }
        }

        private static bool ContainsXmlStatusElement(ResourceWrapper resourceWrapper, string requiredStatusElementName)
        {
            var doc = XDocument.Parse(resourceWrapper.RawResource.Data);

            List<XElement> elementsByName = doc.Root.Elements().Where(x => string.Equals(x.Name.LocalName, requiredStatusElementName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!elementsByName.Any())
            {
                return false;
            }

            // No duplicated status properties are expected.
            XElement status = elementsByName.Single();

            // The status element should have an inner structure or attributes.
            if (!status.HasElements && !status.HasAttributes)
            {
                return false;
            }

            return true;
        }

        private static bool ContainsJsonStatusElement(ResourceWrapper resourceWrapper, string requiredStatusElementName)
        {
            var jsonResource = JObject.Parse(resourceWrapper.RawResource.Data);
            if (!jsonResource.ContainsKey(requiredStatusElementName))
            {
                return false;
            }

            JToken value = jsonResource[requiredStatusElementName];

            if (value == null)
            {
                return false;
            }
            else if (value is JObject jValueObject)
            {
                return jValueObject.Count > 0 && !string.IsNullOrEmpty(jValueObject.ToString());
            }
            else
            {
                return !string.IsNullOrEmpty(value.Value<string>());
            }
        }
    }
}
