// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.Throttling
{
    public class ExcludedEndpoint
    {
        public string Method { get; set; }

        public PathString Path { get; set; }
    }
}
