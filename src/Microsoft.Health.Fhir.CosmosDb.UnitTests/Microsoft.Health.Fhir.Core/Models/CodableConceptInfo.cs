// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class CodableConceptInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CodableConceptInfo"/> class.
        /// </summary>
        public CodableConceptInfo()
        {
            Coding = new List<Coding>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CodableConceptInfo"/> class.
        /// </summary>
        /// <param name="coding">The Coding collection.</param>
        public CodableConceptInfo(IEnumerable<Coding> coding)
        {
            EnsureArg.IsNotNull(coding);

            Coding = coding.ToList();
        }

        /// <summary>
        /// Gets the Coding collection.
        /// </summary>
        public ICollection<Coding> Coding { get; }
    }
}
