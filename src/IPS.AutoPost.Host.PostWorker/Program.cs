// IPS.AutoPost.Host.PostWorker — Program.cs
// Full implementation in Task 19.2
var builder = Host.CreateApplicationBuilder(args);
var host = builder.Build();
await host.RunAsync();
