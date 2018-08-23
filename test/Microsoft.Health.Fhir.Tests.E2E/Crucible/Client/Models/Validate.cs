// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible.Client.Models
{
    public class Validate
    {
        public string Resource { get; set; }

        public string[] Methods { get; set; }

        public string[] Extensions { get; set; }

        public string[] Profiles { get; set; }

        public string[] Formats { get; set; }
    }
}
