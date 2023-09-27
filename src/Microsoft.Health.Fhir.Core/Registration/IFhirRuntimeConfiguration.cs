// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Registration
{
    public interface IFhirRuntimeConfiguration
    {
        /// <summary>
        /// Selective Search Parameter.
        /// </summary>
        bool IsSelectiveSearchParameterSupported { get; }

        /// <summary>
        /// Export background worker.
        /// </summary>
        bool IsExportBackgroundWorkedSupported { get; }

        /// <summary>
        /// Customer Key Validation background worker keeps running and checking the health of customer managed key.
        /// </summary>
        bool IsCustomerKeyValidationBackgroudWorkerSupported { get; }
    }
}
