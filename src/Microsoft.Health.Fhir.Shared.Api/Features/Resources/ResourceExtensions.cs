// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Api.Features.Resources
{
    public static class ResourceExtensions
    {
        private const string ChildrenPropertyName = "Children";

        public static IEnumerable<T> GetAllChildren<T>(
            this Resource resource)
        {
            var stack = new Stack<(PropertyInfo propertyInfo, object baseObject)>(resource.GetType().GetProperties().Where(x => x.Name == ChildrenPropertyName).Select(x => (x, resource as object)));
            while (stack.Any())
            {
                var next = stack.Pop();

                if (next.propertyInfo.PropertyType == typeof(IEnumerable<Base>) && next.propertyInfo.Name == ChildrenPropertyName)
                {
                    foreach (object child in (IEnumerable)next.propertyInfo.GetValue(next.baseObject, null))
                    {
                        if (child is T castResourceReference)
                        {
                            yield return castResourceReference;
                            continue;
                        }

                        foreach (PropertyInfo propertyInfo in child.GetType().GetProperties())
                        {
                            stack.Push((propertyInfo, child));
                        }
                    }
                }
            }
        }
    }
}
