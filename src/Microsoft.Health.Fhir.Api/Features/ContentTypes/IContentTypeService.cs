// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.ContentTypes
{
    public interface IContentTypeService
    {
        Task CheckDesiredContentFormatAsync(HttpContext contextHttpContext);

        Task<bool> IsFormatSupportedAsync(ResourceFormat resourceFormat);
    }
}
