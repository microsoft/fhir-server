// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Azure.Cosmos.Linq;

namespace Microsoft.Health.Fhir.Tests.E2E.Extensions
{
    public static class ModelExtensions
    {
#if !R5
        public static Device AssignPatient(this Device device, ResourceReference patient)
        {
#if Stu3 || R4
            device.Patient = patient;
#else
            device.Subject = patient;
#endif
            return device;
        }
#else
        public static Bundle AssignPatient(this Device device, ResourceReference patient, string associationId = null)
        {
            var deviceId = Guid.NewGuid().ToString();

            device.Id = deviceId;
            var bundle = new Bundle
            {
                Type = Bundle.BundleType.Batch,
            };

            bundle.Entry.Add(new Bundle.EntryComponent
            {
                Resource = device,
                Request = new Bundle.RequestComponent
                {
                    Method = Bundle.HTTPVerb.PUT,
                    Url = $"Device/{deviceId}",
                },
            });

            DeviceAssociation resource = new DeviceAssociation();
            resource.Id = associationId ?? Guid.NewGuid().ToString();
            resource.Subject = patient;
            resource.Device = new ResourceReference($"Device/{deviceId}");

            bundle.Entry.Add(new Bundle.EntryComponent
            {
                Resource = resource,
                Request = new Bundle.RequestComponent
                {
                    Method = Bundle.HTTPVerb.POST,
                    Url = $"DeviceAssociation",
                },
            });

            return bundle;
        }
#endif
    }
}
