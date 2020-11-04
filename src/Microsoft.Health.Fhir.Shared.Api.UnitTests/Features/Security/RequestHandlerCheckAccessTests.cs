// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Api.Operations.Versions;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Security
{
    public class RequestHandlerCheckAccessTests
    {
        // These do not follow the pattern because they do not require authorization
        private static readonly Type[] _exceptions =
        {
            typeof(GetCapabilitiesHandler),
            typeof(GetOperationVersionsHandler),
        };

        public static IEnumerable<object[]> GetHandlerTypes()
        {
            var assemblies = new HashSet<Assembly>();

            void Dfs(Assembly a)
            {
                if (assemblies.Add(a))
                {
                    foreach (AssemblyName referencedAssembly in a.GetReferencedAssemblies())
                    {
                        if (referencedAssembly.Name.StartsWith("Microsoft.Health"))
                        {
                            Dfs(Assembly.Load(referencedAssembly));
                        }
                    }
                }
            }

            Dfs(typeof(SecurityProvider).Assembly);

            return assemblies.SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
                .Where(t => !_exceptions.Contains(t))
                .Select(t => new object[] { t });
        }

        [Theory]
        [MemberData(nameof(GetHandlerTypes))]
        public async Task RequestHandlers_WhenNoActionsArePermitted_ThrowUnauthorizedFhirActionException(Type handlerType)
        {
            var handler = CreateObject(handlerType);

            // set the IFhirAuthorizationService field
            var authServiceFields = GetFieldsIncludingFromBaseTypes(handlerType).Where(f => f.FieldType == typeof(IFhirAuthorizationService));
            FieldInfo authServiceField = Assert.Single(authServiceFields);
            authServiceField.SetValue(handler, Substitute.For<IFhirAuthorizationService>());

            IEnumerable<Type[]> typeArgumentSets = handlerType.GetInterfaces()
                .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .Select(t => t.GetGenericArguments());

            foreach (Type[] typeArguments in typeArgumentSets)
            {
                // Invoke IRequestHandler.Handle without reflection so exceptions are not wrapped in a TargetInvocationException.
                MethodInfo genericMethod = new Func<object, object>(CallHandle<IRequest<object>, object>).Method.GetGenericMethodDefinition().MakeGenericMethod(typeArguments);
                var callHandle = (Func<object, Task>)Delegate.CreateDelegate(typeof(Func<object, Task>), genericMethod);
                await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => callHandle(handler));
            }

            static async Task CallHandle<TRequest, TResponse>(object handler)
                where TRequest : IRequest<TResponse>
            {
                var typedHandler = (IRequestHandler<TRequest, TResponse>)handler;
                var request = (TRequest)CreateObject(typeof(TRequest));
                await typedHandler.Handle(request, CancellationToken.None);
            }

            static IEnumerable<FieldInfo> GetFieldsIncludingFromBaseTypes(Type t)
            {
                for (Type i = t; i != null; i = i.BaseType)
                {
                    foreach (FieldInfo fieldInfo in i.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        yield return fieldInfo;
                    }
                }
            }

            static object CreateObject(Type type)
            {
                return FormatterServices.GetSafeUninitializedObject(type);
            }
        }
    }
}
