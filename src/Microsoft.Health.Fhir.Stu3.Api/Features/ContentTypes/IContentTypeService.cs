// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Stu3.Api.Features.ContentTypes
{
    public interface IContentTypeService
    {
        Task CheckRequestedContentTypeAsync(HttpContext httpContext);

        Task<bool> IsFormatSupportedAsync(ResourceFormat resourceFormat);
    }
}
