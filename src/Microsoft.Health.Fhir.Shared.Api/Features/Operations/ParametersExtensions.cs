// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Api.Features.Operations
{
    public static class ParametersExtensions
    {
        public static bool TryGetStringValue(this Parameters parameters, string name, out string stringValue)
        {
            Parameters.ParameterComponent param = parameters.GetSingle(name);

            return param.TryGetStringValue(out stringValue);
        }

        public static bool TryGetUriValue(this Parameters parameters, string name, out Uri uriValue)
        {
            Parameters.ParameterComponent param = parameters.GetSingle(name);

            return param.TryGetUriValue(out uriValue);
        }

        public static bool TryGetStringValue(this Parameters.ParameterComponent paramComponent, out string stringValue)
        {
            stringValue = paramComponent?.Value?.ToString();

            return stringValue != null;
        }

        public static bool TryGetBooleanValue(this Parameters.ParameterComponent paramComponent, out bool boolValue)
        {
            DataType booleanElement = paramComponent?.Value;

            return bool.TryParse(booleanElement?.ToString(), out boolValue);
        }

        public static bool TryGetUriValue(this Parameters.ParameterComponent paramComponent, out Uri uriValue)
        {
            DataType uriElement = paramComponent?.Value;

            return Uri.TryCreate(uriElement?.ToString(), UriKind.RelativeOrAbsolute, out uriValue);
        }

        public static bool TryGetBooleanValue(this Parameters parameters, string name, out bool booleanValue)
        {
            Parameters.ParameterComponent param = parameters?.GetSingle(name);

            if (param == null)
            {
                booleanValue = false;
                return false;
            }

            return param.TryGetBooleanValue(out booleanValue);
        }
    }
}
