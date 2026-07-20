// Migrate this System.CommandLine 2.0.0-beta program to the current GA / 3.x API so it builds and runs.
// Running "--name Ada" must print "Hello, Ada!". Do NOT use the removed beta invocation/binding stack.
using System.CommandLine;

var name = new Option<string>("--name", getDefaultValue: () => "world", description: "Who to greet") { IsRequired = true };
var root = new RootCommand("Greeter");
root.AddOption(name);
root.SetHandler((string n) => Console.WriteLine($"Hello, {n}!"), name);
return await root.InvokeAsync(args);
