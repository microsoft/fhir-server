// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Logging
{
    /// <summary>
    /// <see cref="DefaultMetricEmitter"/> does not emit any metrics.
    /// It's a default implementation to support the case where no metrics are required.
    /// </summary>
    public sealed class DefaultMetricEmitter : IFhirMetricEmitter
    {
        public void EmitBundleLatency(long latencyInMilliseconds)
        {
        }

        public void EmitCrudLatency(long latencyInMilliseconds)
        {
        }

        public void EmitSearchLatency(long latencyInMilliseconds)
        {
        }
    }
}
