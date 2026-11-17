// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Health.Fhir.Core.Features.Partitions;

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    internal sealed class PartitionRouteConvention : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application)
        {
            foreach (ControllerModel controller in application.Controllers)
            {
                if (controller.Attributes.OfType<PartitionIndependentAttribute>().Any())
                {
                    continue;
                }

                var controllerRoutes = controller.Selectors.Where(selector => selector.AttributeRouteModel != null).ToArray();
                foreach (ActionModel action in controller.Actions)
                {
                    if (action.Attributes.OfType<PartitionIndependentAttribute>().Any())
                    {
                        continue;
                    }

                    foreach (SelectorModel selector in action.Selectors.ToArray())
                    {
                        selector.EndpointMetadata.Add(new PartitionRouteMetadata(selector.AttributeRouteModel?.Name, false));
                        if (selector.AttributeRouteModel?.IsAbsoluteTemplate == true || controllerRoutes.Length == 0)
                        {
                            AddPartitionSelector(action, selector, null);
                        }
                        else
                        {
                            foreach (SelectorModel controllerSelector in controllerRoutes)
                            {
                                AddPartitionSelector(action, selector, controllerSelector);
                            }
                        }
                    }
                }
            }
        }

        private static void AddPartitionSelector(ActionModel action, SelectorModel selector, SelectorModel controllerSelector)
        {
            AttributeRouteModel route = AttributeRouteModel.CombineAttributeRouteModel(controllerSelector?.AttributeRouteModel, selector.AttributeRouteModel);
            if (route == null)
            {
                return;
            }

            var partitionSelector = new SelectorModel(selector);
            if (controllerSelector != null)
            {
                foreach (var constraint in controllerSelector.ActionConstraints)
                {
                    partitionSelector.ActionConstraints.Add(constraint);
                }

                for (int i = 0; i < controllerSelector.EndpointMetadata.Count; i++)
                {
                    partitionSelector.EndpointMetadata.Insert(i, controllerSelector.EndpointMetadata[i]);
                }
            }

            partitionSelector.AttributeRouteModel = new AttributeRouteModel(route)
            {
                // An absolute template avoids combining the controller prefix a second time.
                Template = "/" + PartitionRouteMetadata.TemplatePrefix + "/" + route.Template,
                Name = route.Name == null ? null : PartitionRouteMetadata.NamePrefix + route.Name,
            };
            partitionSelector.EndpointMetadata.Add(new PartitionRouteMetadata(route.Name, true));
            action.Selectors.Add(partitionSelector);
        }
    }
}
