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
            var namedChildren = resource.NamedChildren;
            foreach (var child in namedChildren)
            {
                var childValue = child.Value;
                if (childValue.TypeName == "Reference")
                {
                    var reference = (ResourceReference)childValue;
                    if (reference.Reference != null && reference.Reference.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        reference.Reference = null;
                        reference.Display = "Referenced resource deleted";
                    }
                }
                else if (childValue.Children.Any())
                {
                    RemoveReference(childValue, target);
                }
            }
        }
    }
}
