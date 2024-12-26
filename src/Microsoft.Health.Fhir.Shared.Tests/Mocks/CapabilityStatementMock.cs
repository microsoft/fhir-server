﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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

        public static void SetupMockResource(
            CapabilityStatement capability,
            Hl7.Fhir.Model.ResourceType? resourceType,
            IEnumerable<TypeRestfulInteraction> interactions,
            IEnumerable<SearchParamComponent> searchParams = null,
            ResourceVersionPolicy? versioningPolicy = null)
        {
            capability.Rest[0].Resource.Add(new ResourceComponent
            {
#if Stu3
                Type = resourceType,
#elif R4
                Type = resourceType.ToString(),
#elif R5
                Type = resourceType.ToString(),
#elif R4B
                Type = resourceType.ToString(),
#endif
                Interaction = interactions?.Select(x => new ResourceInteractionComponent { Code = x }).ToList(),
                SearchParam = searchParams?.ToList(),
                Versioning = versioningPolicy,
            });
        }
    }
}
