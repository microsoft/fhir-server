// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Api.Extensions
{
    internal static class ExceptionExtensions
    {
        public static Exception GetInnerMostException(this Exception input)
        {
            EnsureArg.IsNotNull(input, nameof(input));

            Exception current = input;

            while (current.InnerException != null)
            {
                current = current.InnerException;
            }

            return current;
        }
    }
}
