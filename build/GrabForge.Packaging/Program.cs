using Microsoft.NET.HostModel.AppHost;

if (args.Length != 3) throw new ArgumentException("Usage: <SDK apphost template> <root executable> <published managed assembly>");
// Keep the SDK's native launcher and self-contained runtime; no second process or installed runtime is needed.
HostWriter.CreateAppHost(args[0], args[1], "app/GrabForge.dll",
    windowsGraphicalUserInterface: true, assemblyToCopyResourcesFrom: args[2],
    dotNetSearchOptions: new HostWriter.DotNetSearchOptions { Location = HostWriter.DotNetSearchOptions.SearchLocation.AppLocal });
Console.WriteLine("Created portable root executable.");
