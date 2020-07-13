// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    public class RawBundleFhirResult : ResourceActionResult<RawSearchBundle>
    {
        public RawBundleFhirResult()
        {
        }

        public RawBundleFhirResult(RawSearchBundle resource)
            : base(resource)
        {
        }

        public static RawBundleFhirResult Create(RawSearchBundle resource, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return new RawBundleFhirResult(resource)
            {
                StatusCode = statusCode,
            };
        }

        /// <summary>
        /// Creates a Gone response
        /// </summary>
        public static RawBundleFhirResult Gone()
        {
            return new RawBundleFhirResult
            {
                StatusCode = HttpStatusCode.Gone,
            };
        }

        /// <summary>
        /// Returns a NotFound response
        /// </summary>
        public static RawBundleFhirResult NotFound()
        {
            return new RawBundleFhirResult
            {
                StatusCode = HttpStatusCode.NotFound,
            };
        }

        /// <summary>
        /// Returns a NoContent response
        /// </summary>
        public static RawBundleFhirResult NoContent()
        {
            return new RawBundleFhirResult
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
            return "Bundle";
        }
    }
}
