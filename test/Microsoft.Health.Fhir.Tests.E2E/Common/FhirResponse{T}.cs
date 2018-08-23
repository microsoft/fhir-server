// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public class FhirResponse<T> : FhirResponse
        where T : Resource
    {
        public FhirResponse(HttpResponseMessage response, T resource)
            : base(response)
        {
            Resource = resource;
        }

        public T Resource { get; }

        public static implicit operator T(FhirResponse<T> response)
        {
            return response.Resource;
        }
    }
}
