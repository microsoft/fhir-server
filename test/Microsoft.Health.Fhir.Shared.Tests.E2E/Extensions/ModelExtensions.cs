// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Tests.E2E.Extensions
{
    public static class ModelExtensions
    {
        public static Resource AssignPatient(this Device device, ResourceReference patient)
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

#if Stu3 || R4
            device.Patient = patient;
#else
            DeviceAssociation resource = new DeviceAssociation();
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
#endif
            return bundle;
        }
    }
}
