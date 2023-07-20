// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Rest;

namespace Microsoft.Health.Fhir.Core.Models
{
    /// <summary>
    /// Compares instances of <see cref="BundleResourceContext"/> to support the correct sequence of resources.
    /// </summary>
    public sealed class BundleResourceContextComparer : IComparer<BundleResourceContext>
    {
        private static HTTPVerb[] _verbExecutionOrder = new HTTPVerb[] { HTTPVerb.DELETE, HTTPVerb.POST, HTTPVerb.PUT, HTTPVerb.PATCH, HTTPVerb.GET, HTTPVerb.HEAD };

        public int Compare(BundleResourceContext x, BundleResourceContext y)
        {
            if (x == null && y == null)
            {
                return 0;
            }
            else if (x == null)
            {
                return -1;
            }
            else if (y == null)
            {
                return 1;
            }
            else if (x == y)
            {
                return 0;
            }
            else
            {
                int xMethodIndex = Array.IndexOf(_verbExecutionOrder, x.HttpVerb);
                int yMethodIndex = Array.IndexOf(_verbExecutionOrder, y.HttpVerb);

                return xMethodIndex.CompareTo(yMethodIndex);
            }
        }
    }
}
