// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Web;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// The current / default partitioning strategy
    /// </summary>
    public class ResourceIdPartitioningStrategy : IPartitioningStrategy
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;

        public ResourceIdPartitioningStrategy(RequestContextAccessor<IFhirRequestContext> requestContextAccessor)
        {
            _requestContextAccessor = requestContextAccessor;
        }

        public bool AllowsCrossPartitionQueries => true;

        public string GetSearchPartitionOrNull()
        {
            var requestContext = _requestContextAccessor.RequestContext;
            var nameValueCollection = HttpUtility.ParseQueryString(_requestContextAccessor.RequestContext.Uri.Query);
            var id = nameValueCollection.Get(KnownQueryParameterNames.Id);

            if (!string.IsNullOrEmpty(id))
            {
                return $"{requestContext.ResourceType}_{id}";
            }

            return null;
        }

        public string GetStoragePartition(ResourceWrapper resource)
        {
            return $"{resource.ResourceTypeName}_{resource.ResourceId}";
        }
    }
}
