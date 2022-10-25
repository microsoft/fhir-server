// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
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
                                    Core.Resources.USCoreMissingDataRequirement));
                            continue;
                        }
                    }
                    catch (JsonReaderException jre)
                    {
                        searchIssues.Add(
                            new OperationOutcomeIssue(
                                OperationOutcomeConstants.IssueSeverity.Error,
                                OperationOutcomeConstants.IssueType.Incomplete,
                                string.Format(Core.Resources.USCoreDeserializationError, jre.Message)));
                        continue;
                    }
                }

                finalResults.Add(resultEntry);
            }

            // Add new data type to handle outcomes.
            return new SearchResult(
                searchResult.Results,
                searchResult.ContinuationToken,
                searchResult.SortOrder,
                searchResult.UnsupportedSearchParameters);
        }

        private static bool IsRecordFromEligibleResourceType(string resourceTypeName, out string requiredStatusElementName) =>
            _requiredStatusElementsByResourceType.TryGetValue(resourceTypeName, out requiredStatusElementName);

        private static bool ContainStatusElement(ResourceWrapper resourceWrapper, string requiredStatusElementName)
        {
            EnsureArg.IsNotNull(resourceWrapper);
            EnsureArg.IsNotEmptyOrWhiteSpace(requiredStatusElementName);

            if (resourceWrapper.RawResource.Format == Models.FhirResourceFormat.Json)
            {
                JObject jsonResource = JObject.Parse(resourceWrapper.RawResource.Data);
                if (jsonResource.ContainsKey(requiredStatusElementName))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            // FERNFE: Should we check the same for records in XML format?
            return true;
        }
    }
}
