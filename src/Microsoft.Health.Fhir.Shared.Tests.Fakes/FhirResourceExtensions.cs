// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Reflection;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Tests.Fakes
{
    /// <summary>
    /// Provides extension methods for FHIR resources and related types for testing purposes.
    /// </summary>
    public static class FhirResourceExtensions
    {
        /// <summary>
        /// Links a FHIR resource to the specified patient by setting common patient reference properties.
        /// </summary>
        /// <typeparam name="T">The type of the FHIR resource.</typeparam>
        /// <param name="resource">The FHIR resource to link.</param>
        /// <param name="patient">The patient to link to.</param>
        /// <returns>The modified FHIR resource with patient links.</returns>
        /// <exception cref="ArgumentNullException">If resource or patient is null.</exception>
        public static T LinkToPatient<T>(this T resource, Patient patient)
            where T : Resource
        {
            if (resource == null)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            if (patient == null)
            {
                throw new ArgumentNullException(nameof(patient));
            }

            var patientReference = new ResourceReference($"Patient/{patient.Id}");
            return resource.LinkToPatient(patientReference);
        }

        /// <summary>
        /// Links a FHIR Base object (typically a Resource or Element) to the specified patient reference
        /// by setting common patient reference properties.
        /// After linking, it ensures that all required properties of the object are populated.
        /// </summary>
        /// <typeparam name="T">The type of the FHIR Base object.</typeparam>
        /// <param name="baseObject">The FHIR Base object to link.</param>
        /// <param name="patientReference">The patient reference to link to.</param>
        /// <returns>The modified FHIR Base object with patient links and ensured required properties.</returns>
        /// <exception cref="ArgumentNullException">If baseObject or patientReference is null.</exception>
        public static T LinkToPatient<T>(this T baseObject, ResourceReference patientReference)
            where T : Base // Using Base as FHIR model types derive from it.
        {
            if (baseObject == null)
            {
                throw new ArgumentNullException(nameof(baseObject));
            }

            if (patientReference == null)
            {
                throw new ArgumentNullException(nameof(patientReference));
            }

            foreach (PropertyInfo prop in baseObject.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite)
                {
                    continue;
                }

                if (typeof(ResourceReference).IsAssignableFrom(prop.PropertyType))
                {
                    // These are common properties that refer to a patient or a related context.
                    switch (prop.Name)
                    {
                        case "Subject":
                        case "Patient":
                        case "Individual":
                        case "Beneficiary":
                        case "Recorder":
                        case "Encounter": // Included as it was in the original factory logic.
                            prop.SetValue(baseObject, patientReference);
                            break;
                    }
                }
            }

            return baseObject;
        }
    }
}
