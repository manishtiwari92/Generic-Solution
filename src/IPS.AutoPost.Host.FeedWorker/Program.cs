// IPS.AutoPost.Host.FeedWorker — Program.cs
// Full implementation in Task 18.2
var builder = Host.CreateApplicationBuilder(args);
var host = builder.Build();
await host.RunAsync();
