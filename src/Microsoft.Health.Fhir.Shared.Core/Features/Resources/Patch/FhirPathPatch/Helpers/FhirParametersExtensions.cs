// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers
{
    public static class FhirParametersExtensions
    {
        public static Parameters AddPatchParameter(this Parameters parameters, string operationType, string path = "", string name = "", object value = null)
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
                if (value is DataType valueDataType)
                {
                    component.Part.Add(
                        new Parameters.ParameterComponent
                        {
                            Name = "value",
                            Value = valueDataType,
                        });
                }
                else if (value is Parameters.ParameterComponent valueParameter)
                {
                    component.Part.Add(
                        new Parameters.ParameterComponent
                        {
                            Name = "value",
                            Part = new List<Parameters.ParameterComponent> { valueParameter },
                        });
                }
                else if (value is List<Parameters.ParameterComponent> valueParameterList)
                {
                    component.Part.Add(
                        new Parameters.ParameterComponent
                        {
                            Name = "value",
                            Part = valueParameterList,
                        });
                }
                else
                {
                    throw new ArgumentOutOfRangeException($"Value must be of type {typeof(DataType)} or {typeof(Parameters.ParameterComponent)}.");
                }
            }

            parameters.Parameter.Add(component);
            return parameters;
        }
    }
}
