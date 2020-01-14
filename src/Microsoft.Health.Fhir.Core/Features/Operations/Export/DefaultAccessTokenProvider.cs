// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class DefaultAccessTokenProvider : IAccessTokenProvider
    {
        public string GetAccessTokenForResource(Uri resourceUri)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            return "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        }
    }
}
