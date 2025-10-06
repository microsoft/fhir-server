// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Registration
{
    public interface IFhirRuntimeConfiguration
    {
        string DataStore { get; }

        /// <summary>
        /// Selective Search Parameter.
        /// </summary>
        bool IsSelectiveSearchParameterSupported { get; }

        /// <summary>
        /// Customer Key Validation background worker keeps running and checking the health of customer managed key.
        /// </summary>
        bool IsCustomerKeyValidationBackgroundWorkerSupported { get; }

        /// <summary>
        /// Support to transactions.
        /// </summary>
        bool IsTransactionSupported { get; }

        /// <summary>
        /// Supports the 'latency-over-efficiency' HTTP header.
        /// </summary>
        bool IsLatencyOverEfficiencySupported { get; }

        /// <summary>
        /// Supports the query cache HTTP header.
        /// </summary>
        bool IsQueryCacheSupported { get; }

        /// <summary>
        /// Search Service's support for surrogate ID ranging.
        /// </summary>
        bool IsSurrogateIdRangingSupported { get; }
    }
}
