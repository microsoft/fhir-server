// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using static Hl7.Fhir.Model.Parameters;

namespace FhirPathPatch.Operations
{
    /// <summary>
    /// An object to represent the operation type and parameters for a FHIR
    /// Path Patch operation to be completed.
    /// </summary>
    public class PendingOperation
    {
        /// <summary>
        /// Gets or sets the enumeration value for this instances operation type.
        /// </summary>
        public EOperationType Type { get; set; }

        /// <summary>
        /// Gets or sets the parameter component of the pending operation.
        /// </summary>
        public ParameterComponent Parameter { get; set; }

        /// <summary>
        /// Gets or sets the operation index.
        /// </summary>
        public int? Index { get; set; }

        /// <summary>
        /// Gets or sets the source index.
        /// </summary>
        public int? Source { get; set; }

        /// <summary>
        /// Gets or sets the destination index.
        /// </summary>
        public int? Destination { get; set; }

        /// <summary>
        /// Gets or sets the operation name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the operation path.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets the operation value as the input ParameterComponent.
        /// </summary>
        public ParameterComponent Value { get; set; }

        /// <summary>
        /// Convert from ParameterComponent to PendingOperation Object.
        /// </summary>
        /// <param name="component">ParameterComponent.</param>
        /// <returns>PendingOperation.</returns>
        public static PendingOperation FromParameterComponent(ParameterComponent component)
        {
            var c = new CultureInfo("en-US", false);
            var operationType = component.Part.First(x => x.Name == "type").Value.ToString().ToUpper(c);
            var path = component.Part.First(x => x.Name == "path").Value.ToString();
            var name = component.Part.FirstOrDefault(x => x.Name == "name")?.Value.ToString();
            var value = component.Part.FirstOrDefault(x => x.Name == "value");

            int? index = int.TryParse(component.Part.FirstOrDefault(x => x.Name == "index")?.Value.ToString(), out int itmp) ? itmp : null;
            int? source = int.TryParse(component.Part.FirstOrDefault(x => x.Name == "source")?.Value.ToString(), out int stmp) ? stmp : null;
            int? destination = int.TryParse(component.Part.FirstOrDefault(x => x.Name == "destination")?.Value.ToString(), out int dtmp) ? dtmp : null;

            return new PendingOperation
            {
                Type = (EOperationType)Enum.Parse(typeof(EOperationType), operationType),
                Parameter = component,
                Path = path,
                Name = name,
                Value = value,
                Index = index,
                Source = source,
                Destination = destination,
            };
        }
    }
}
