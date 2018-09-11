// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Extensions.DependencyInjection
{
    public class TypeRegistration
    {
        private readonly IServiceCollection _serviceCollection;
        private readonly Func<IServiceProvider, object> _delegateRegistration;

        internal TypeRegistration(IServiceCollection serviceCollection, Type type)
        {
            EnsureArg.IsNotNull(serviceCollection, nameof(serviceCollection));
            EnsureArg.IsNotNull(type, nameof(type));

            _serviceCollection = serviceCollection;
            Type = type;
        }

        internal TypeRegistration(IServiceCollection serviceCollection, Type returnType, Func<IServiceProvider, object> delegateRegistration)
        {
            EnsureArg.IsNotNull(serviceCollection, nameof(serviceCollection));
            EnsureArg.IsNotNull(delegateRegistration, nameof(delegateRegistration));

            _serviceCollection = serviceCollection;
            _delegateRegistration = delegateRegistration;
            Type = returnType;
        }

        public enum RegistrationMode
        {
#pragma warning disable SA1602 // Enumeration items must be documented
            Transient,
            Scoped,
            Singleton,
#pragma warning restore SA1602 // Enumeration items must be documented
        }

        public Type Type { get; }

        public TypeRegistrationBuilder Singleton()
        {
            return new TypeRegistrationBuilder(_serviceCollection, Type, _delegateRegistration, RegistrationMode.Singleton);
        }

        public TypeRegistrationBuilder Scoped()
        {
            return new TypeRegistrationBuilder(_serviceCollection, Type, _delegateRegistration, RegistrationMode.Scoped);
        }

        public TypeRegistrationBuilder Transient()
        {
            return new TypeRegistrationBuilder(_serviceCollection, Type, _delegateRegistration, RegistrationMode.Transient);
        }
    }
}
