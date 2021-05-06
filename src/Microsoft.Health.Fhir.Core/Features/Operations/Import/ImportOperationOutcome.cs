// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportOperationOutcome
    {
        public string Type { get; set; }

        public long Count { get; set; }

        public Uri InputUrl { get; set; }

#pragma warning disable CA1056 // URI-like properties should not be strings
        public string Url { get; set; }
#pragma warning restore CA1056 // URI-like properties should not be strings
    }
}
