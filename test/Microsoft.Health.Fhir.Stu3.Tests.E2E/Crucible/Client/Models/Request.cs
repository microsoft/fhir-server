// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible.Client.Models
{
    public class Request
    {
        public string Method { get; set; }

        public string Url { get; set; }

        public Headers Headers { get; set; }

        public string Path { get; set; }

        public string Payload { get; set; }
    }
}
