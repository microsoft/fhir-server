// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    public class RawBundleFhirResult : ResourceActionResult<JRaw>
    {
        public RawBundleFhirResult()
        {
        }

        public RawBundleFhirResult(JRaw resource)
            : base(resource)
        {
        }

        public static RawBundleFhirResult Create(JRaw resource, HttpStatusCode statusCode = HttpStatusCode.OK)
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
