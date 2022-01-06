// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class FhirParametersExtensions
    {
        public static Parameters AddPatchParameter(this Parameters parameters, string operationType, string path = "", string name = "", DataType value = null)
        {
            var component = new Parameters.ParameterComponent
            {
                Name = "operation",
                Part = new List<Parameters.ParameterComponent>
                    {
                        new Parameters.ParameterComponent
                        {
                            Name = "type",
                            Value = new FhirString(operationType),
                        },
                    },
            };

            if (!string.IsNullOrEmpty(path))
            {
                component.Part.Add(
                    new Parameters.ParameterComponent
                    {
                        Name = "path",
                        Value = new FhirString(path),
                    });
            }

            if (!string.IsNullOrEmpty(name))
            {
                component.Part.Add(
                    new Parameters.ParameterComponent
                    {
                        Name = "name",
                        Value = new FhirString(name),
                    });
            }

            if (value is not null)
            {
                component.Part.Add(
                    new Parameters.ParameterComponent
                    {
                        Name = "value",
                        Value = value,
                    });
            }

            parameters.Parameter.Add(component);
            return parameters;
        }
    }
}
