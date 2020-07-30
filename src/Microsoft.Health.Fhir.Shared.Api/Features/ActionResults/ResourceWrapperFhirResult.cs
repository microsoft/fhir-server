// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    public class ResourceWrapperFhirResult : ResourceActionResult<ResourceWrapper>
    {
        public ResourceWrapperFhirResult()
        {
        }

        public ResourceWrapperFhirResult(ResourceWrapper resource)
            : base(resource)
        {
        }

        public static ResourceWrapperFhirResult Create(ResourceWrapper resource, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return new ResourceWrapperFhirResult(resource)
            {
                StatusCode = statusCode,
            };
        }

        /// <summary>
        /// Creates a Gone response
        /// </summary>
        public static ResourceWrapperFhirResult Gone()
        {
            return new ResourceWrapperFhirResult
            {
                StatusCode = HttpStatusCode.Gone,
            };
        }

        /// <summary>
        /// Returns a NotFound response
        /// </summary>
        public static ResourceWrapperFhirResult NotFound()
        {
            return new ResourceWrapperFhirResult
            {
                StatusCode = HttpStatusCode.NotFound,
            };
        }

        /// <summary>
        /// Returns a NoContent response
        /// </summary>
        public static ResourceWrapperFhirResult NoContent()
        {
            return new ResourceWrapperFhirResult
            {
                StatusCode = HttpStatusCode.NoContent,
            };
        }

        protected override object GetResultToSerialize()
        {
            return Result;
        }

        public override string GetResultTypeName()
        {
            return Result?.ResourceTypeName;
        }
    }
}
