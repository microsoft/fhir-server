// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Namotion.Reflection;
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
                                    Core.Resources.USCoreMissingDataRequirement));
                            continue;
                        }
                    }
                    catch (JsonReaderException jsonE)
                    {
                        searchIssues.Add(
                            new OperationOutcomeIssue(
                                OperationOutcomeConstants.IssueSeverity.Error,
                                OperationOutcomeConstants.IssueType.Incomplete,
                                string.Format(Core.Resources.USCoreDeserializationError, jsonE.Message)));
                        continue;
                    }
                    catch (XmlException xmlE)
                    {
                        searchIssues.Add(
                            new OperationOutcomeIssue(
                                OperationOutcomeConstants.IssueSeverity.Error,
                                OperationOutcomeConstants.IssueType.Incomplete,
                                string.Format(Core.Resources.USCoreDeserializationError, xmlE.Message)));
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
                return ContainsJsonElement(resourceWrapper, requiredStatusElementName);
            }
            else if (resourceWrapper.RawResource.Format == FhirResourceFormat.Xml)
            {
                return ContainsXmlElement(resourceWrapper, requiredStatusElementName);
            }
            else
            {
                throw new ArgumentException($"Format '{resourceWrapper.RawResource.Format}' is a not supported format");
            }
        }

        private static bool ContainsXmlElement(ResourceWrapper resourceWrapper, string requiredStatusElementName)
        {
            XmlDocument doc = new XmlDocument();

            using (XmlReader reader = XmlReader.Create(resourceWrapper.RawResource.Data))
            {
                doc.Load(reader);
                if (!doc.Attributes.HasProperty(requiredStatusElementName))
                {
                    return false;
                }

                string value = doc.Attributes[requiredStatusElementName].Value;
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        private static bool ContainsJsonElement(ResourceWrapper resourceWrapper, string requiredStatusElementName)
        {
            JObject jsonResource = JObject.Parse(resourceWrapper.RawResource.Data);
            if (!jsonResource.ContainsKey(requiredStatusElementName))
            {
                return false;
            }

            JToken value = jsonResource[requiredStatusElementName];
            return value != null;
        }
    }
}
