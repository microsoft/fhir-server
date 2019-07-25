// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    internal static class OperationStatusExtensions
    {
        internal static bool IsFinished(this OperationStatus operationStatus)
        {
            switch (operationStatus)
            {
                case OperationStatus.Canceled:
                case OperationStatus.Completed:
                case OperationStatus.Failed:
                    return true;

                default:
                    return false;
            }
        }
    }
}
