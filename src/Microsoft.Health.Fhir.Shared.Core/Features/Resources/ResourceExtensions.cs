// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Resources
{
    public static class ResourceExtensions
    {
        public static IEnumerable<T> GetAllChildren<T>(this Base resource)
            where T : class
        {
            foreach (Base child in resource.Children)
            {
                if (child is T targetTypeObject)
                {
                    yield return targetTypeObject;
                }

                foreach (T subChild in child.GetAllChildren<T>())
                {
                    yield return subChild;
                }
            }
        }
    }
}
