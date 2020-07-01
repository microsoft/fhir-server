// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public interface IAnonymizer
    {
        /// <summary>
        /// Initialize anonymizer
        /// </summary>
        Task InitailizeAsync();

        /// <summary>
        /// Anonymize the FHIR resource
        /// </summary>
        /// <param name="resourceElement">The FHIR resource for anonymization.</param>
        /// <returns>The anonymized FHIR resource.</returns>
        ResourceElement Anonymize(ResourceElement resourceElement);
    }
}
