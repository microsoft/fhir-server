// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class SearchParameterCapability
    {
        public SearchParameterCapability(string resourceType, string name, string type, string documentation = null)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));
            EnsureArg.IsNotNullOrEmpty(name, nameof(name));
            EnsureArg.IsNotNullOrEmpty(type, nameof(type));

            ResourceType = resourceType;
            Name = name;
            Type = type;
            Documentation = documentation;
        }

        public string ResourceType { get; }

        public string Name { get; }

        public string Type { get; }

        public string Documentation { get; }
    }
}
