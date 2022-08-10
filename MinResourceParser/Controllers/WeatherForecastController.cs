// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace MinResourceParser.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching",
        };

        private IResourceWrapperFactory _resourceWrapperFactory;
        private readonly ILogger<WeatherForecastController> _logger;

        public WeatherForecastController(IResourceWrapperFactory resourceWrapperFactory, ILogger<WeatherForecastController> logger)
        {
            _resourceWrapperFactory = resourceWrapperFactory;
            _logger = logger;
        }

        [HttpPost(Name = "PostResource")]
        public IEnumerable<WeatherForecast> Get([FromBody] Resource resource)
        {
            _resourceWrapperFactory.Create(new ResourceElement(resource.ToTypedElement()), false, true);

            // Leftover from prebuilt code
#pragma warning disable CA5394 // Do not use insecure randomness
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateTime.Now.AddDays(index),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)],
            })
            .ToArray();
#pragma warning restore CA5394 // Do not use insecure randomness
        }
    }
}
