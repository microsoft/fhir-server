// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace FHIRDataSynth
{
    public class FHIRDataSynthException : Exception
    {
        public FHIRDataSynthException(string message)
            : base(message)
        {
        }

        public FHIRDataSynthException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public FHIRDataSynthException(string resourceGroupDir, string resourceName, string resourceId, string message)
            : base($"{resourceGroupDir}/{resourceName}/{resourceId}: {message}")
        {
        }
    }
}
