// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Bundle
{
    public sealed class BundleResponseInfo
    {
        public BundleResponseInfo(TimeSpan executionTime, string bundleType, BundleProcessingLogic processingLogic)
        {
            ExecutionTime = executionTime;
            BundleType = bundleType;
            ProcessingLogic = processingLogic;
        }

        public TimeSpan ExecutionTime { get; }

        public BundleProcessingLogic ProcessingLogic { get; }

        public string BundleType { get; }
    }
}
