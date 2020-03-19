// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    internal class AadSmartOnFhirClaimsExtractor : IClaimsExtractor
    {
        private const string ClientId = "client_id";

        private readonly IHttpContextAccessor _httpContextAccessor;

        public AadSmartOnFhirClaimsExtractor(IHttpContextAccessor httpContextAccessor)
        {
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));

            _httpContextAccessor = httpContextAccessor;
        }

        public IReadOnlyCollection<KeyValuePair<string, string>> Extract()
        {
            HttpContext context = _httpContextAccessor.HttpContext;

            StringValues clientId = context.Request.HasFormContentType ? context.Request.Form[ClientId] : context.Request.Query[ClientId];

            ReadOnlyCollection<KeyValuePair<string, string>> claims = clientId.Select(x => new KeyValuePair<string, string>(ClientId, x)).ToList().AsReadOnly();
            return claims;
        }
    }
}
