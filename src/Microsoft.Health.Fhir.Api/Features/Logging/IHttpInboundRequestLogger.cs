// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.Logging
{
    public interface IHttpInboundRequestLogger
    {
        void LogRequest(HttpContext context, Exception exception = null);
    }
}
