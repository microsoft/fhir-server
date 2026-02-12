// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Stats;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Resources
{
    /// <summary>
    /// Extension methods for converting StatsResponse to FHIR resources.
    /// </summary>
    public static class StatsResponseExtensions
    {
        private const string ResourceTypeParameterName = "resourceType";
        private const string TotalCountParameterName = "totalCount";
        private const string ActiveCountParameterName = "activeCount";

        /// <summary>
        /// Converts a StatsResponse to a FHIR Parameters resource.
        /// </summary>
        /// <param name="response">The stats response to convert.</param>
        /// <returns>A ResourceElement containing a FHIR Parameters resource.</returns>
        public static ResourceElement ToParameters(this StatsResponse response)
        {
            var parameters = new Parameters();

            foreach (var resourceStat in response.ResourceStats)
            {
                var resourceTypeComponent = new Parameters.ParameterComponent
                {
                    Name = ResourceTypeParameterName,
                };

                resourceTypeComponent.Part.Add(new Parameters.ParameterComponent
                {
                    Name = "name",
                    Value = new FhirString(resourceStat.Key),
                });

                resourceTypeComponent.Part.Add(new Parameters.ParameterComponent
                {
                    Name = TotalCountParameterName,
                    Value = new Integer64(resourceStat.Value.TotalCount),
                });

                resourceTypeComponent.Part.Add(new Parameters.ParameterComponent
                {
                    Name = ActiveCountParameterName,
                    Value = new Integer64(resourceStat.Value.ActiveCount),
                });

                parameters.Parameter.Add(resourceTypeComponent);
            }

            return parameters.ToResourceElement();
        }
    }
}
