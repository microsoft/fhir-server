// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Resources
{
    public static class ResourceExtensions
    {
        public static IEnumerable<T> GetAllChildren<T>(this Base resource)
            where T : class
        {
            foreach (object child in resource.EnumerateElements().Select(pair => pair.Value))
            {
                if (child is T targetTypeObject)
                {
                    yield return targetTypeObject;
                }

                if (child is Base childBase)
                {
                    foreach (T subChild in childBase.GetAllChildren<T>())
                    {
                        yield return subChild;
                    }
                }
                else if (child is IEnumerable childList && childList.GetType().IsGenericType)
                {
                    foreach (T subChild in GetChildrenFromList<T>(childList))
                    {
                        yield return subChild;
                    }
                }
                else
                {
                    throw new ArgumentException($"Unrecognized child type {child.GetType()}");
                }
            }
        }

        private static IEnumerable<T> GetChildrenFromList<T>(IEnumerable list)
            where T : class
        {
            foreach (var child in list)
            {
                if (child is T subTargetTypeObject)
                {
                    yield return subTargetTypeObject;
                }

                if (child is Base childBase)
                {
                    foreach (T subChild in childBase.GetAllChildren<T>())
                    {
                        yield return subChild;
                    }
                }
                else if (child is IEnumerable childList && childList.GetType().IsGenericType)
                {
                    foreach (T subChild in GetChildrenFromList<T>(childList))
                    {
                        yield return subChild;
                    }
                }
                else
                {
                    throw new ArgumentException($"Unrecognized child type {child.GetType()}");
                }
            }
        }
    }
}
