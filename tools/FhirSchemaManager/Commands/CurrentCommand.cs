// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.CommandLine.Rendering;
using System.CommandLine.Rendering.Views;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FhirSchemaManager.Exceptions;
using FhirSchemaManager.Model;
using FhirSchemaManager.Utils;
using Newtonsoft.Json;

namespace FhirSchemaManager.Commands
{
    public static class CurrentCommand
    {
        public static async Task HandlerAsync(InvocationContext invocationContext, Uri fhirServer)
        {
            var region = new Region(
                          0,
                          0,
                          Console.WindowWidth,
                          Console.WindowHeight,
                          true);
            List<CurrentVersion> currentVersions = null;

            try
            {
                currentVersions = await GetCurrentVersionInformation(fhirServer);
            }
            catch (SchemaOperationFailedException ex)
            {
                CommandUtils.RenderError(new ErrorDescription((int)ex.StatusCode, ex.Message), invocationContext, region);
                return;
            }
            catch (HttpRequestException)
            {
                CommandUtils.PrintError(Resources.RequestFailedMessage);
                return;
            }

            var tableView = new TableView<CurrentVersion>
            {
                Items = currentVersions,
            };

            tableView.AddColumn(
               cellValue: currentVersion => currentVersion.Id,
               header: new ContentView("Version"));

            tableView.AddColumn(
                cellValue: currentVersion => currentVersion.Status,
                header: new ContentView("Status"));

            tableView.AddColumn(
                cellValue: currentVersion => string.Join(", ", currentVersion.Servers),
                header: new ContentView("Servers"));

            var consoleRenderer = new ConsoleRenderer(
                invocationContext.Console,
                mode: invocationContext.BindingContext.OutputMode(),
                resetAfterRender: true);

            var screen = new ScreenView(renderer: consoleRenderer) { Child = tableView };
            screen.Render(region);
        }

        private static async Task<List<CurrentVersion>> GetCurrentVersionInformation(Uri serverUri)
        {
            var httpClient = new HttpClient
            {
                BaseAddress = serverUri,
            };

            var response = await httpClient.GetAsync(new Uri("/_schema/versions/current", UriKind.Relative));
            if (response.IsSuccessStatusCode)
            {
                var responseBodyAsString = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<CurrentVersion>>(responseBodyAsString);
            }
            else
            {
                switch (response.StatusCode)
                {
                    case HttpStatusCode.NotFound:
                        throw new SchemaOperationFailedException(response.StatusCode, Resources.CurrentInformationNotFound);

                    default:
                        throw new SchemaOperationFailedException(response.StatusCode, Resources.CurrentDefaultErrorDescription);
                }
            }
        }
    }
}
