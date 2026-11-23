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
                // Controller-level routes (e.g., SchemaController's "_schema") are combined below; multiple controller routes are not supported.
                AttributeRouteModel controllerRoute = controller.Selectors.SingleOrDefault(x => x.AttributeRouteModel != null)?.AttributeRouteModel;

                foreach (ActionModel action in controller.Actions.Where(x => !x.Attributes.OfType<PartitionIndependentAttribute>().Any()))
                {
                    foreach (SelectorModel selector in action.Selectors)
                    {
                        AttributeRouteModel route = AttributeRouteModel.CombineAttributeRouteModel(controllerRoute, selector.AttributeRouteModel);

                        selector.AttributeRouteModel = new AttributeRouteModel(route)
                        {
                            // An absolute template prevents MVC from combining the controller route a second time.
                            Template = "/" + AttributeRouteModel.CombineTemplates(PartitionRoute.TemplatePrefix, route.Template),
                        };
                    }
                }
            }
        }
    }
}
