// *** CLIENT PROJECT — BgQuiz_Blazor.Client (WASM) ***
//
// Phase 2a scaffolding only. This host wires the minimum needed to render one
// board client-side; the full quiz flow (file picker, problem-set source,
// controller) stays server-side until Phase 2b, gated on this spike's verdict.

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
