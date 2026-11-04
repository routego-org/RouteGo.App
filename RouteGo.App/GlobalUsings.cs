// Slice files carry no using directives — this file holds the shared set.
// The sibling slice skill and database setup EXTEND this list beneath the '// FILLED BY' marker below.
global using System.Text.Json;          // JsonSerializer, WireJson.Options
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.Mvc.RazorPages;
global using RouteGo.App.Common;             // HttpClients, WireJson
global using RouteGo.App.Common.Ports;       // IClock
// FILLED BY skills-aspnetcore-ui-vertical-slice — and by skills-db-postgres (copy-assets.ps1) at database setup:
global using System.Net.Http.Json;
global using FluentValidation;
global using FluentValidation.Results;   // ValidationResult in OnPostAsync
global using RouteGo.App.Common.UI;             // SliceRouteConvention, helpers, Flash
