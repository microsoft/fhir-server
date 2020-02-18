// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class Role
    {
        public Role(string name, DataActions allowedDataActions, string scope)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));
            EnsureArg.Is(scope, "/", nameof(scope)); // until we support data slices

            Name = name;
            AllowedDataActions = allowedDataActions;
            Scope = scope;
        }

        public string Name { get; }

        public DataActions AllowedDataActions { get; }

        public string Scope { get; }
    }
}
