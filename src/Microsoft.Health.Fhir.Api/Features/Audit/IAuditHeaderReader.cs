// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    public interface IAuditHeaderReader
    {
        IReadOnlyDictionary<string, string> Read(HttpContext httpContext);
    }
}
