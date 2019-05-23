// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.ContentTypes
{
    public interface IContentTypeService
    {
        Task CheckRequestedContentTypeAsync(HttpContext httpContext);

        Task<bool> IsFormatSupportedAsync(string resourceFormat);
    }
}
