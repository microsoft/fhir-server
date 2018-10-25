// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public static class EnvironmentVariables
    {
        public static string GetEnvironmentVariableWithDefault(string environmentVariableName, string defaultValue)
        {
            var environmentVariable = Environment.GetEnvironmentVariable(environmentVariableName);

            return string.IsNullOrWhiteSpace(environmentVariable) ? defaultValue : environmentVariable;
        }
    }
}
