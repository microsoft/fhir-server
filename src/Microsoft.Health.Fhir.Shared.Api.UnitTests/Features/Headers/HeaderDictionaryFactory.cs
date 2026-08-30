// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.ActionResults;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Headers
{
    public static class HeaderDictionaryFactory
    {
        public static IHeaderDictionary Create()
        {
            // Replacement of regular HeaderDictionary.
            return new ThreadSafeHeaderDictionary();
        }
    }
}
