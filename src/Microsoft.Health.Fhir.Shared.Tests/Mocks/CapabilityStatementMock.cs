// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using static Hl7.Fhir.Model.CapabilityStatement;

namespace Microsoft.Health.Fhir.Tests.Common.Mocks
{
    public class CapabilityStatementMock
    {
        public static CapabilityStatement GetMockedCapabilityStatement()
        {
            return new CapabilityStatement
            {
                Rest = new List<RestComponent>
                {
                    new RestComponent
                    {
                        Resource = new List<ResourceComponent>(),
                    },
                },
            };
        }
    }
}
