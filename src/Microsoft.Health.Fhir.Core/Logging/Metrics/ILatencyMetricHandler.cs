// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Antlr4.Runtime.Tree.Xpath;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public interface ILatencyMetricHandler<T>
         where T : ILatencyMetricNotification
    {
        void EmitLatency(T notification);
    }
}
