// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Health.Fhir.Api.Features.Partitions;

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    internal sealed class PartitionRouteConvention : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application)
        {
            foreach (ControllerModel controller in application.Controllers.Where(x => !x.Attributes.OfType<PartitionIndependentAttribute>().Any()))
            {
                foreach (ActionModel action in controller.Actions.Where(x => !x.Attributes.OfType<PartitionIndependentAttribute>().Any()))
                {
                    foreach (SelectorModel selector in action.Selectors)
                    {
                        AttributeRouteModel route = selector.AttributeRouteModel;

                        selector.AttributeRouteModel = new AttributeRouteModel(route)
                        {
                            // An absolute template avoids combining the controller prefix a second time.
                            Template = "/" + PartitionRouteMetadata.TemplatePrefix + "/" + route.Template,
                        };
                        selector.EndpointMetadata.Add(new PartitionRouteMetadata(route.Name, true));
                    }
                }
            }
        }
    }
}
