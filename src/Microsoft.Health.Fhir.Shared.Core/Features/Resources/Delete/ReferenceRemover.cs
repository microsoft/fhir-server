// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public static class ReferenceRemover
    {
        public static void RemoveReference(Base resource, string target)
        {
            RemoveReferenceRecursive(resource, target);
        }

        private static void RemoveReferenceRecursive(Base resource, string target)
        {
            if (resource == null)
            {
                return;
            }

            // Check if this is a ResourceReference
            if (resource is ResourceReference reference)
            {
                if (reference.Reference != null && reference.Reference.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    reference.Reference = null;
                    reference.Display = "Referenced resource deleted";
                }
            }

            // Recursively process all properties
            var properties = resource.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

            foreach (var prop in properties)
            {
                var value = prop.GetValue(resource);
                if (value == null)
                {
                    continue;
                }

                if (value is Base baseValue)
                {
                    RemoveReferenceRecursive(baseValue, target);
                }
                else if (value is System.Collections.IEnumerable enumerable && !(value is string))
                {
                    foreach (var item in enumerable)
                    {
                        if (item is Base baseItem)
                        {
                            RemoveReferenceRecursive(baseItem, target);
                        }
                    }
                }
            }
        }
    }
}
