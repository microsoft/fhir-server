// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient
{
    public class UnsupportedDestinationTypeException : Exception
    {
        public UnsupportedDestinationTypeException(string destinationType)
            : base(string.Format(Resources.UnsupportedDestinationType, destinationType))
        {
        }
    }
}
