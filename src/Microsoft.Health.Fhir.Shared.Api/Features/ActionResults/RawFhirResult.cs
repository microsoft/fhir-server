// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    public class RawFhirResult : ResourceActionResult<ResourceWrapper>
    {
        public RawFhirResult()
        {
        }

        public RawFhirResult(ResourceWrapper resource)
            : base(resource)
        {
        }

        public static RawFhirResult Create(ResourceWrapper resource, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return new RawFhirResult(resource)
            {
                StatusCode = statusCode,
            };
        }

        /// <summary>
        /// Creates a Gone response
        /// </summary>
        public static RawFhirResult Gone()
        {
            return new RawFhirResult
            {
                StatusCode = HttpStatusCode.Gone,
            };
        }

        /// <summary>
        /// Returns a NotFound response
        /// </summary>
        public static RawFhirResult NotFound()
        {
            return new RawFhirResult
            {
                StatusCode = HttpStatusCode.NotFound,
            };
        }

        /// <summary>
        /// Returns a NoContent response
        /// </summary>
        public static RawFhirResult NoContent()
        {
            return new RawFhirResult
            {
                StatusCode = HttpStatusCode.NoContent,
            };
        }

        protected override object GetResultToSerialize()
        {
            return Result.RawResource.Data;
        }

        public override string GetResultTypeName()
        {
            return Result?.ResourceTypeName;
        }
    }
}
