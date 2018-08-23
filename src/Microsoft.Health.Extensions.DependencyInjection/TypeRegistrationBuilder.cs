// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Extensions.DependencyInjection
{
    public class TypeRegistrationBuilder
    {
        private readonly IServiceCollection _serviceCollection;
        private readonly Type _type;
        private readonly Func<IServiceProvider, object> _delegateRegistration;
        private readonly TypeRegistration.RegistrationMode _registrationMode;

        private Func<IServiceProvider, object> _cachedResolver;
        private Type _firstRegisteredType;

        internal TypeRegistrationBuilder(
            IServiceCollection serviceCollection,
            Type type,
            Func<IServiceProvider, object> delegateRegistration,
            TypeRegistration.RegistrationMode registrationMode)
        {
            _serviceCollection = serviceCollection;
            _type = type;
            _delegateRegistration = delegateRegistration;
            _registrationMode = registrationMode;
        }

        /// <summary>
        /// Creates a service registration for the specified interface
        /// </summary>
        /// <typeparam name="T">Type of service to be registered</typeparam>
        /// <returns>The registration builder</returns>
        public TypeRegistrationBuilder AsService<T>()
        {
            Debug.Assert(typeof(T) != _type, $"The \"AsSelf()\" registration should be used instead of \"AsService<{_type.Name}>()\".");

            RegisterType(typeof(T));

            return this;
        }

        /// <summary>
        /// Creates a service registration for all interfaces implemented by the type
        /// </summary>
        /// <param name="interfaceFilter">A predicate specifying which interfaces to register.</param>
        /// <returns>The registration builder</returns>
        /// <exception cref="NotSupportedException">Throws when Type was not explicitly defined</exception>
        public TypeRegistrationBuilder AsImplementedInterfaces(Predicate<Type> interfaceFilter = null)
        {
            Debug.Assert(
                _firstRegisteredType != null || _registrationMode == TypeRegistration.RegistrationMode.Transient,
                $"Using \"AsImplementedInterfaces()\" without calling \"AsSelf()\" first in registration for \"{_type.Name}\" could have inconsistent results.");

            var interfaces = _type.GetInterfaces().Where(x => interfaceFilter == null || interfaceFilter(x));

            foreach (var typeInterface in interfaces)
            {
                RegisterType(typeInterface);
            }

            return this;
        }

        /// <summary>
        /// Create a service registration for the concrete type
        /// </summary>
        /// <returns>The registration builder</returns>
        /// <exception cref="NotSupportedException">Throws when Type was not explicitly defined</exception>
        public TypeRegistrationBuilder AsSelf()
        {
            Debug.Assert(_firstRegisteredType == null, $"The \"AsSelf()\" registration for \"{_type.Name}\" should come first.");

            RegisterType(_type);

            return this;
        }

        private void RegisterType(Type serviceType)
        {
            EnsureArg.IsNotNull(serviceType, nameof(serviceType));

            if (_firstRegisteredType == null && _registrationMode != TypeRegistration.RegistrationMode.Transient)
            {
                SetupRootRegistration(serviceType);

                return;
            }

            switch (_registrationMode)
            {
                case TypeRegistration.RegistrationMode.Transient:

                    if (_delegateRegistration != null)
                    {
                        _serviceCollection.AddTransient(serviceType, _delegateRegistration);
                    }
                    else
                    {
                        _serviceCollection.AddTransient(serviceType, _type);
                    }

                    break;
                case TypeRegistration.RegistrationMode.Scoped:

                    _serviceCollection.AddScoped(serviceType, _cachedResolver);

                    break;
                case TypeRegistration.RegistrationMode.Singleton:

                    _serviceCollection.AddSingleton(serviceType, _cachedResolver);

                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        private void SetupRootRegistration(Type serviceType)
        {
            _firstRegisteredType = serviceType;

            if (_delegateRegistration == null)
            {
                if (_registrationMode == TypeRegistration.RegistrationMode.Scoped)
                {
                    _serviceCollection.AddScoped(serviceType, _type);
                }
                else if (_registrationMode == TypeRegistration.RegistrationMode.Singleton)
                {
                    _serviceCollection.AddSingleton(serviceType, _type);
                }
            }
            else
            {
                if (_registrationMode == TypeRegistration.RegistrationMode.Scoped)
                {
                    _serviceCollection.AddScoped(serviceType, _delegateRegistration);
                }
                else if (_registrationMode == TypeRegistration.RegistrationMode.Singleton)
                {
                    _serviceCollection.AddSingleton(serviceType, _delegateRegistration);
                }
            }

            _cachedResolver = provider => provider.GetService(_firstRegisteredType);
        }
    }
}
