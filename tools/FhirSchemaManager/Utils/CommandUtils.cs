// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.CommandLine.Rendering;
using System.CommandLine.Rendering.Views;
using FhirSchemaManager.Exceptions;

namespace FhirSchemaManager.Utils
{
    public static class CommandUtils
    {
        public static void PrintError(string errorMessage)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(errorMessage);
            Console.ResetColor();
        }

        public static void RenderError(SchemaManagerException schemaException, InvocationContext invocationContext, Region region)
        {
            var tableView = new TableView<SchemaManagerException>
            {
                Items = new List<SchemaManagerException>() { schemaException },
            };

            tableView.AddColumn(
               cellValue: schemaManagerException => schemaManagerException.StatusCode,
               header: new ContentView("HttpStatusCode"));

            tableView.AddColumn(
                cellValue: schemaManagerException => schemaManagerException.Message,
                header: new ContentView("ErrorMessage"));

            Console.ForegroundColor = ConsoleColor.Red;

            var consoleRenderer = new ConsoleRenderer(
                invocationContext.Console,
                mode: invocationContext.BindingContext.OutputMode(),
                resetAfterRender: true);

            var screen = new ScreenView(renderer: consoleRenderer) { Child = tableView };
            screen.Render(region);

            Console.ResetColor();
        }
    }
}
