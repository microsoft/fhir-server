// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class ResourcePermission
    {
        public ResourcePermission()
        {
            Actions = new HashSet<ResourceAction>();
        }

        public ResourcePermission(HashSet<ResourceAction> resourceActions)
        {
            Actions = resourceActions;
        }

        public HashSet<ResourceAction> Actions { get; }

        public string Filter { get; set; }
    }
}
