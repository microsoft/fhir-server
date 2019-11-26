// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.Bundle
{
    public class BundleHttpContextAccessor : IBundleHttpContextAccessor
    {
        private readonly AsyncLocal<HttpContext> _httpContextCurrent = new AsyncLocal<HttpContext>();

        public HttpContext HttpContext
        {
            get => _httpContextCurrent.Value;

            set => _httpContextCurrent.Value = value;
        }
    }
}
