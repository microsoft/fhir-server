// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public static class PartialDataExtensions
    {
        private const string IsPartialContent = "IsPartialResponse";

        public static RequestContextAccessor<IFhirRequestContext> SetMissingResourceCode(this RequestContextAccessor<IFhirRequestContext> requestContextAccessor, HttpStatusCode code)
        {
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));

            requestContextAccessor.RequestContext.Properties[IsPartialContent] = code;

            return requestContextAccessor;
        }

        public static HttpStatusCode? GetMissingResourceCode(this RequestContextAccessor<IFhirRequestContext> requestContextAccessor)
        {
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));

            if (requestContextAccessor.RequestContext.Properties.TryGetValue(IsPartialContent, out var value))
            {
                return (HttpStatusCode)value;
            }

            return null;
        }
    }
}
