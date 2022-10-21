// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

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

            if (!_isCriteriaEnabled || !_isSmartRequest)
            {
                return searchResult;
            }

            foreach (SearchResultEntry resultEntry in searchResult.Results)
            {
                if (_requiredStatusElementsByResourceType.TryGetValue(resultEntry.Resource.ResourceTypeName, out string requiredStatusElementName))
                {
                    continue;
                }
            }

            return searchResult;
        }

        private static bool DoesResourceContainStatusElement(ResourceWrapper resourceWrapper, string requiredStatusElementName)
        {
            EnsureArg.IsNotNull(resourceWrapper);
            EnsureArg.IsNotEmptyOrWhiteSpace(requiredStatusElementName);

            if (resourceWrapper.RawResource.Format == Models.FhirResourceFormat.Json)
            {

            }

            // FERNFE: Should we check the same for records in XML format?
            return true;
        }
    }
}
