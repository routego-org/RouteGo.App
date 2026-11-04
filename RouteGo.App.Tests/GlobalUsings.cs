// Slice test files (linked in from {Project}/Features/**/*.Tests.cs) carry no using directives —
// this file holds the test project's shared set. The sibling slice skill EXTENDS it as its
// test helpers need more (e.g. Testcontainers, AngleSharp).
global using Xunit;
global using RouteGo.App.Common;             // HttpClients, WireJson
global using RouteGo.App.Common.Testing;
global using RouteGo.App.Common.UI;
global using RouteGo.App.Tests.Testing;
