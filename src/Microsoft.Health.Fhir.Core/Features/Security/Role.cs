// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class Role
    {
        public Role(string name, FhirActions allowedActions)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));

            Name = name;
            AllowedActions = allowedActions;
        }

        public string Name { get; }

        public FhirActions AllowedActions { get; }
    }
}
