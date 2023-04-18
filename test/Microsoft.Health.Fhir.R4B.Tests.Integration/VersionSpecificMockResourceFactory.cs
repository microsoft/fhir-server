// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using static Hl7.Fhir.Model.CapabilityStatement;

namespace Microsoft.Health.Fhir.Tests.Integration
{
    internal class VersionSpecificMockResourceFactory
    {
        public static void SetupMockResource(
            CapabilityStatement capability,
            ResourceType type,
            IEnumerable<TypeRestfulInteraction> interactions,
            IEnumerable<SearchParamComponent> searchParams = null,
            ResourceVersionPolicy? versioningPolicy = null)
        {
            capability.Rest[0].Resource.Add(new ResourceComponent
            {
                Type = type.ToString(),
                Interaction = interactions?.Select(x => new ResourceInteractionComponent { Code = x }).ToList(),
                SearchParam = searchParams?.ToList(),
                Versioning = versioningPolicy,
            });
        }
    }
}
