// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Extensions;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    /// <summary>
    /// Provides the ability to lookup audit event type.
    /// </summary>
    public class AuditEventTypeMapping : IAuditEventTypeMapping, IStartable
    {
        private readonly IActionDescriptorCollectionProvider _actionDescriptorCollectionProvider;

        private IReadOnlyDictionary<(string ControllerName, string ActionName), Attribute> _attributeDictionary;

        public AuditEventTypeMapping(IActionDescriptorCollectionProvider actionDescriptorCollectionProvider)
        {
            EnsureArg.IsNotNull(actionDescriptorCollectionProvider, nameof(actionDescriptorCollectionProvider));

            _actionDescriptorCollectionProvider = actionDescriptorCollectionProvider;
        }

        /// <inheritdoc />
        public string GetAuditEventType(string controllerName, string actionName)
        {
            if (!_attributeDictionary.TryGetValue((controllerName, actionName), out Attribute attribute))
            {
                throw new MissingAuditEventTypeMappingException(controllerName, actionName);
            }

            if (attribute is AuditEventTypeAttribute auditEventTypeAttribute)
            {
                return auditEventTypeAttribute.AuditEventType;
            }

            return null;
        }

        void IStartable.Start()
        {
            _attributeDictionary = _actionDescriptorCollectionProvider.ActionDescriptors.Items
                .OfType<ControllerActionDescriptor>()
                .Select(ad =>
                {
                    Attribute attribute = ad.MethodInfo?.GetCustomAttributes<AllowAnonymousAttribute>().FirstOrDefault() ??
                        (Attribute)ad.MethodInfo?.GetCustomAttributes<AuditEventTypeAttribute>().FirstOrDefault();

                    return (ad.ControllerName, ad.ActionName, Attribute: attribute);
                })
                .Where(item => item.Attribute != null)
                .ToDictionary(
                    item => (item.ControllerName, item.ActionName),
                    item => item.Attribute);
        }
    }
}
