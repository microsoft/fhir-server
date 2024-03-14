// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Logging
{
    public interface IFhirMetricEmitter
    {
        void EmitBundleLatency(long latencyInMilliseconds);

        void EmitSearchLatency(long latencyInMilliseconds);

        void EmitCrudLatency(long latencyInMilliseconds);
    }
}
