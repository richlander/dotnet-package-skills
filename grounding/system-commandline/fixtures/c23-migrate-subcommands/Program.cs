// Migrate this beta System.CommandLine program to the current GA / 3.x API so it builds and runs.
// Running "add 3" must print "added 3". Remove the beta invocation/binding stack.
using System.CommandLine;

var item = new Argument<int>("item");
var add = new Command("add", "Add an item");
add.AddArgument(item);
var verbose = new Option<bool>("--verbose", description: "Verbose output");
var root = new RootCommand("Item tool");
root.AddGlobalOption(verbose);
root.AddCommand(add);
add.SetHandler((int n) => Console.WriteLine($"added {n}"), item);
return await root.InvokeAsync(args);
