// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Partitions data by Patient Compartment.
    /// Search queries require a parameter from the compartment to identify the partition.
    /// Resources without Patient compartment definitions are written to "default" partition.
    /// </summary>
    public class PatientCompartmentPartitioningStrategy : IPartitioningStrategy
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly ICompartmentDefinitionManager _compartmentDefinitionManager;
        private readonly IReferenceSearchValueParser _referenceSearchValueParser;

        private const string DefaultPartitionId = "default";

        public PatientCompartmentPartitioningStrategy(
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            ICompartmentDefinitionManager compartmentDefinitionManager,
            IReferenceSearchValueParser referenceSearchValueParser)
        {
            _requestContextAccessor = requestContextAccessor;
            _compartmentDefinitionManager = compartmentDefinitionManager;
            _referenceSearchValueParser = referenceSearchValueParser;
        }

        public bool AllowsCrossPartitionQueries
        {
            get
            {
                return _requestContextAccessor.RequestContext.RequestHeaders.Keys.Contains("x-allow-cross-partition-queries");
            }
        }

        public string GetSearchPartitionOrNull()
        {
            IFhirRequestContext requestContext = _requestContextAccessor.RequestContext;
            NameValueCollection nameValueCollection = HttpUtility.ParseQueryString(_requestContextAccessor.RequestContext.Uri.Query);

            if (!string.IsNullOrEmpty(requestContext.ResourceType))
            {
                if (string.Equals(requestContext.ResourceType, KnownResourceTypes.Patient, StringComparison.Ordinal))
                {
                    var id = nameValueCollection[KnownQueryParameterNames.Id];
                    if (!string.IsNullOrEmpty(id))
                    {
                        return id;
                    }
                }

                if (_compartmentDefinitionManager.TryGetSearchParams(requestContext.ResourceType, CompartmentType.Patient, out HashSet<string> searchParams))
                {
                    var firstCompartmentParameter = nameValueCollection.AllKeys.FirstOrDefault(x => searchParams.Contains(x));

                    if (!string.IsNullOrEmpty(firstCompartmentParameter))
                    {
                        var reference = _referenceSearchValueParser.Parse(nameValueCollection[firstCompartmentParameter]);
                        return reference.ResourceId;
                    }
                }
            }

            if (_compartmentDefinitionManager.TryGetResourceTypes(CompartmentType.Patient, out HashSet<string> resourceTypes) &&
                resourceTypes.Contains(requestContext.ResourceType))
            {
                if (AllowsCrossPartitionQueries)
                {
                    return null;
                }

                throw new RequestNotValidException("Search request requires a parameter from Patient compartment.");
            }

            return DefaultPartitionId;
        }

        public string GetStoragePartition(ResourceWrapper resource)
        {
            if (string.Equals(resource.ResourceTypeName, KnownResourceTypes.Patient, StringComparison.Ordinal))
            {
                return resource.ResourceId;
            }

            if (_compartmentDefinitionManager.TryGetSearchParams(resource.ResourceTypeName, CompartmentType.Patient, out HashSet<string> searchParams))
            {
                var values = resource.SearchIndices
                    .Where(x => x.SearchParameter.Type == SearchParamType.Reference)
                    .FirstOrDefault(x => searchParams.Contains(x.SearchParameter.Code))?.Value as ReferenceSearchValue;

                if (values != null)
                {
                    return values.ResourceId;
                }
            }

            if (_compartmentDefinitionManager.TryGetResourceTypes(CompartmentType.Patient, out HashSet<string> resourceTypes) &&
                resourceTypes.Contains(resource.ResourceTypeName))
            {
                throw new RequestNotValidException("A Patient compartment is required for this resource.");
            }

            return DefaultPartitionId;
        }
    }
}
