// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible.Client.Models
{
    public class Response
    {
        public object Code { get; set; }

        public Headers Headers { get; set; }

        public string Body { get; set; }
    }
}
