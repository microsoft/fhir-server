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
        private static readonly IDictionary<string, string> _requiredStatusElementsByResourceType = new Dictionary<string, string>()
        {
            { "AllergyIntolerance", "clinicalStatus" },
            { "Condition", "clinicalStatus" },
            { "DocumentReference", "status" },
            { "Immunization", "status" },
            { "Goal", "lifecycleStatus" },
        };

        private readonly bool _isCriteriaEnabled;

        private readonly bool _isSmartRequest;

        public MissingDataFilterCriteria(bool isCriteriaEnabled, bool isSmartRequest)
        {
            _isCriteriaEnabled = isCriteriaEnabled;
            _isSmartRequest = isSmartRequest;
        }

        public SearchResult Apply(SearchResult searchResult)
        {
            EnsureArg.IsNotNull(searchResult);

            if (!_isCriteriaEnabled || !_isSmartRequest || !searchResult.Results.Any())
            {
                return searchResult;
            }

            List<SearchResultEntry> finalResults = new List<SearchResultEntry>();
            List<OperationOutcomeIssue> searchIssues = new List<OperationOutcomeIssue>();

            foreach (SearchResultEntry resultEntry in searchResult.Results)
            {
                if (IsRecordFromEligibleResourceType(resultEntry.Resource.ResourceTypeName, out string requiredStatusElementName))
                {
                    try
                    {
                        if (!ContainStatusElement(resultEntry.Resource, requiredStatusElementName))
                        {
                            // Resource does not contain the required status code.
                            searchIssues.Add(
                                new OperationOutcomeIssue(
                                    OperationOutcomeConstants.IssueSeverity.Error,
                                    OperationOutcomeConstants.IssueType.NotFound,
                                    string.Format(
                                        Core.Resources.USCoreMissingDataRequirement,
                                        resultEntry.Resource.ResourceTypeName,
                                        resultEntry.Resource.ResourceId)));
                            continue;
                        }
                    }
                    catch (JsonReaderException jsonE)
                    {
                        searchIssues.Add(
                            new OperationOutcomeIssue(
                                OperationOutcomeConstants.IssueSeverity.Error,
                                OperationOutcomeConstants.IssueType.Incomplete,
                                string.Format(Core.Resources.USCoreDeserializationError, resultEntry.Resource.ResourceId, jsonE.Message)));
                        continue;
                    }
                    catch (XmlException xmlE)
                    {
                        searchIssues.Add(
                            new OperationOutcomeIssue(
                                OperationOutcomeConstants.IssueSeverity.Error,
                                OperationOutcomeConstants.IssueType.Incomplete,
                                string.Format(Core.Resources.USCoreDeserializationError, resultEntry.Resource.ResourceId, xmlE.Message)));
                        continue;
                    }
                }

                finalResults.Add(resultEntry);
            }

            return new SearchResult(
                results: finalResults,
                searchResult.ContinuationToken,
                searchResult.SortOrder,
                searchResult.UnsupportedSearchParameters,
                searchIssues: searchIssues);
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
            XDocument doc = XDocument.Parse(resourceWrapper.RawResource.Data);

            IEnumerable<XElement> elementsByName = doc.Root.Elements().Where(x => string.Equals(x.Name.LocalName, requiredStatusElementName, StringComparison.OrdinalIgnoreCase));

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
            JObject jsonResource = JObject.Parse(resourceWrapper.RawResource.Data);
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
