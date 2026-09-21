// Composition root. Story 1.1 stands up an empty host so the Api ring compiles and the
// dependency rules are asserted against a real ASP.NET Core assembly graph.
// Routes, auth, OpenAPI, and the ProblemDetails mapping arrive with Story 1.2.

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

WebApplication app = builder.Build();

app.Run();
