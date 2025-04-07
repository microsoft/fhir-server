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
        public static Hl7.Fhir.Model.Parameters AddPatchParameter(this Hl7.Fhir.Model.Parameters parameters, string operationType, string path = "", string name = "", object value = null)
        {
            var component = new Hl7.Fhir.Model.Parameters.ParameterComponent
            {
                Name = "operation",
                Part = new List<Hl7.Fhir.Model.Parameters.ParameterComponent>
                    {
                        new Hl7.Fhir.Model.Parameters.ParameterComponent
                        {
                            Name = "type",
                            Value = new FhirString(operationType),
                        },
                    },
            };

            if (!string.IsNullOrEmpty(path))
            {
                component.Part.Add(
                    new Hl7.Fhir.Model.Parameters.ParameterComponent
                    {
                        Name = "path",
                        Value = new FhirString(path),
                    });
            }

            if (!string.IsNullOrEmpty(name))
            {
                component.Part.Add(
                    new Hl7.Fhir.Model.Parameters.ParameterComponent
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
                        new Hl7.Fhir.Model.Parameters.ParameterComponent
                        {
                            Name = "value",
                            Value = valueDataType,
                        });
                }
                else if (value is Hl7.Fhir.Model.Parameters.ParameterComponent valueParameter)
                {
                    component.Part.Add(
                        new Hl7.Fhir.Model.Parameters.ParameterComponent
                        {
                            Name = "value",
                            Part = new List<Hl7.Fhir.Model.Parameters.ParameterComponent> { valueParameter },
                        });
                }
                else if (value is List<Hl7.Fhir.Model.Parameters.ParameterComponent> valueParameterList)
                {
                    component.Part.Add(
                        new Hl7.Fhir.Model.Parameters.ParameterComponent
                        {
                            Name = "value",
                            Part = valueParameterList,
                        });
                }
                else
                {
                    throw new ArgumentOutOfRangeException($"Value must be of type {typeof(DataType)} or {typeof(Hl7.Fhir.Model.Parameters.ParameterComponent)}.");
                }
            }

            parameters.Parameter.Add(component);
            return parameters;
        }
    }
}
