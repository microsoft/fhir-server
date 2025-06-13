// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public static class ReferenceRemover
    {
        public static void RemoveReference(Base resource, string target)
        {
            foreach (var child in resource.Children)
            {
                if (child.TypeName == "reference") // this is not the correct string, use debugger to find the right value
                {
                    var reference = (ResourceReference)child;
                    if (reference.Reference.Contains(target, StringComparison.OrdinalIgnoreCase))
                    {
                        reference.Reference = string.Empty;
                        reference.Display = "Referenced resource was deleted.";
                    }
                }
                else if (child.Children.Any())
                {
                    RemoveReference(child, target);
                }
            }
        }
    }
}
