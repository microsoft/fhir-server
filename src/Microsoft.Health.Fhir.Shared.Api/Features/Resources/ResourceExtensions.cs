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
            var stack = new Stack<(PropertyInfo propertyInfo, object baseObject)>(resource.GetType().GetProperties().Select(x => (x, resource as object)));
            while (stack.Any())
            {
                var next = stack.Pop();

                if (next.propertyInfo.PropertyType == typeof(T))
                {
                    if (next.propertyInfo.GetValue(next.baseObject) is T resourceReference)
                    {
                        yield return resourceReference;
                    }
                }

                if (next.propertyInfo.PropertyType == typeof(List<T>))
                {
                    foreach (object resourceReference in (IEnumerable)next.propertyInfo.GetValue(next.baseObject, null))
                    {
                        if (resourceReference is T castResourceReference)
                        {
                            yield return castResourceReference;
                        }
                    }
                }

                if (next.propertyInfo.PropertyType == typeof(IEnumerable<Base>) && next.propertyInfo.Name == ChildrenPropertyName)
                {
                    foreach (object child in (IEnumerable)next.propertyInfo.GetValue(next.baseObject, null))
                    {
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
