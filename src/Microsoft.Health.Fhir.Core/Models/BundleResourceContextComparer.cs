// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Hl7.Fhir.Rest;
using Microsoft.VisualBasic;

namespace Microsoft.Health.Fhir.Core.Models
{
    /// <summary>
    /// Compares instances of <see cref="BundleResourceContext"/> to support the correct sequence of resources.
    /// </summary>
    public sealed class BundleResourceContextComparer : IComparer<BundleResourceContext>
    {
        /// <summary>
        /// Because of the rules that a transaction is atomic where all actions pass or fail together and the order of the entries doesn't matter,
        /// there is a particular order in which to process the actions:
        /// 1. Process any delete (DELETE) interactions
        /// 2. Process any create(POST) interactions
        /// 3. Process any update(PUT) or patch(PATCH) interactions
        /// 4. Process any read, vread, search or history(GET or HEAD) interactions
        /// Reference: https://www.hl7.org/fhir/http.html#trules
        /// </summary>
        private static HTTPVerb[] _verbExecutionSequence = new HTTPVerb[] { HTTPVerb.DELETE, HTTPVerb.POST, HTTPVerb.PUT, HTTPVerb.PATCH, HTTPVerb.GET, HTTPVerb.HEAD };

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
                int xMethodIndex = Array.IndexOf(_verbExecutionSequence, x.HttpVerb);
                int yMethodIndex = Array.IndexOf(_verbExecutionSequence, y.HttpVerb);

                return xMethodIndex.CompareTo(yMethodIndex);
            }
        }
    }
}
