// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Audit
{
    public class AuditEventTypeMappingTests
    {
        private const string ControllerName = nameof(MockController);
        private const string AnonymousMethodName = nameof(MockController.Anonymous);
        private const string AudittedMethodName = nameof(MockController.Auditted);
        private const string NoAttributeMethodName = nameof(MockController.NoAttribute);
        private const string AuditEventType = "audit";

        private readonly IActionDescriptorCollectionProvider _actionDescriptorCollectionProvider = Substitute.For<IActionDescriptorCollectionProvider>();
        private readonly AuditEventTypeMapping _auditEventTypeMapping;

        public AuditEventTypeMappingTests()
        {
            Type mockControllerType = typeof(MockController);

            var actionDescriptors = new List<ActionDescriptor>()
            {
                new ControllerActionDescriptor()
                {
                    ControllerName = ControllerName,
                    ActionName = AnonymousMethodName,
                    MethodInfo = mockControllerType.GetMethod(AnonymousMethodName),
                },
                new ControllerActionDescriptor()
                {
                    ControllerName = ControllerName,
                    ActionName = AudittedMethodName,
                    MethodInfo = mockControllerType.GetMethod(AudittedMethodName),
                },
                new ControllerActionDescriptor()
                {
                    ControllerName = ControllerName,
                    ActionName = NoAttributeMethodName,
                    MethodInfo = mockControllerType.GetMethod(NoAttributeMethodName),
                },
                new PageActionDescriptor()
                {
                },
            };

            var actionDescriptorCollection = new ActionDescriptorCollection(actionDescriptors, 1);

            _actionDescriptorCollectionProvider.ActionDescriptors.Returns(actionDescriptorCollection);

            _auditEventTypeMapping = new AuditEventTypeMapping(_actionDescriptorCollectionProvider);

            ((IStartable)_auditEventTypeMapping).Start();
        }

        [Theory]
        [InlineData(ControllerName, AnonymousMethodName, null)]
        [InlineData(ControllerName, AudittedMethodName, AuditEventType)]
        public void GivenControllerNameAndActionName_WhenGetAuditEventTypeIsCalled_ThenAuditEventTypeShouldBeReturned(string controllerName, string actionName, string expectedAuditEventType)
        {
            string actualAuditEventType = _auditEventTypeMapping.GetAuditEventType(controllerName, actionName);

            Assert.Equal(expectedAuditEventType, actualAuditEventType);
        }

        [Fact]
        public void GivenUnknownControllerNameAndActionName_WhenGetAuditEventTypeIsCalled_ThenAuditExceptionShouldBeThrown()
        {
            Assert.Throws<MissingAuditEventTypeMappingException>(() => _auditEventTypeMapping.GetAuditEventType("test", "action"));
        }

        private class MockController : Controller
        {
            [AllowAnonymous]
            public IActionResult Anonymous() => new OkResult();

            [AuditEventType(AuditEventType)]
            public IActionResult Auditted() => new OkResult();

            public IActionResult NoAttribute() => new OkResult();
        }
    }
}
