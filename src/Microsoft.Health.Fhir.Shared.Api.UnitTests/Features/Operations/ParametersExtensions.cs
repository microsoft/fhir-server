// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.Operations
{
    public static class ParametersExtensions
    {
        public static bool TryGetStringValue(this Parameters parameters, string name, out string stringValue)
        {
            Parameters.ParameterComponent param = parameters.GetSingle(name);
            stringValue = param?.Value?.ToString();

            return stringValue != null;
        }
    }
}
