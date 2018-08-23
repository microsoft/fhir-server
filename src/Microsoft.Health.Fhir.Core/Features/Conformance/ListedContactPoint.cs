// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class ListedContactPoint
    {
        public ContactPoint.ContactPointSystem? System { get; set; }

        public ContactPoint.ContactPointUse? Use { get; set; }

        public string Value { get; set; }
    }
}
