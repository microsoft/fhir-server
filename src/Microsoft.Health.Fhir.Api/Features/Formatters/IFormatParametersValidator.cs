// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    public interface IFormatParametersValidator
    {
        Task CheckRequestedContentTypeAsync(HttpContext httpContext);

        void CheckPrettyParameter(HttpContext httpContext);

        void CheckSummaryParameter(HttpContext httpContext);

        void CheckElementsParameter(HttpContext httpContext);

        Task<bool> IsFormatSupportedAsync(string format);
    }
}
