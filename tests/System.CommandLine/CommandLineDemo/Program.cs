using System.Globalization;
using McMaster.Extensions.CommandLineUtils;

namespace CommandLineDemo;

public static class CommandLineDemoApp
{
    public static CommandLineApplication Build(TextWriter? output = null, TextWriter? error = null)
    {
        var app = new CommandLineApplication
        {
            Name = "command-line-demo",
            Description = "Sample command line app using CommandLineUtils."
        };
        app.HelpOption("-?|-h|--help");

        if (output is not null)
        {
            app.Out = output;
        }

        if (error is not null)
        {
            app.Error = error;
        }

        app.Command("greet", command =>
        {
            command.Description = "Print a greeting.";
            command.Out = app.Out;
            command.Error = app.Error;
            var name = command.Argument("name", "Name to greet.").IsRequired();
            var times = command.Option<int>("-t|--times <COUNT>", "Number of greetings to print.", CommandOptionType.SingleValue);
            var excited = command.Option("--excited", "Add an exclamation point.", CommandOptionType.NoValue);

            command.OnExecute(() =>
            {
                var count = times.HasValue() ? times.ParsedValue : 1;
                var suffix = excited.HasValue() ? "!" : ".";

                for (var i = 0; i < count; i++)
                {
                    command.Out.WriteLine($"Hello, {name.Value}{suffix}");
                }

                return 0;
            });
        });

        app.Command("sum", command =>
        {
            command.Description = "Sum integer values.";
            command.Out = app.Out;
            command.Error = app.Error;
            var numbers = command.Argument("numbers", "Numbers to sum.");
            numbers.MultipleValues = true;
            numbers.IsRequired();

            var absolute = command.Option("--absolute", "Use absolute values.", CommandOptionType.NoValue);
            var format = command.Option("-f|--format <FORMAT>", "Output format: text or json.", CommandOptionType.SingleValue);

            command.OnExecute(() =>
            {
                if (numbers.Values.Count == 0)
                {
                    command.Error.WriteLine("At least one number is required.");
                    return 1;
                }

                var values = new List<int>(numbers.Values.Count);
                foreach (var value in numbers.Values)
                {
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        command.Error.WriteLine($"Invalid number: {value}");
                        return 1;
                    }

                    if (absolute.HasValue())
                    {
                        parsed = Math.Abs(parsed);
                    }

                    values.Add(parsed);
                }

                var sum = values.Sum();
                var formatValue = format.HasValue() ? format.Value() : "text";

                if (string.Equals(formatValue, "json", StringComparison.OrdinalIgnoreCase))
                {
                    command.Out.WriteLine($@"{{""sum"":{sum}}}");
                    return 0;
                }

                if (string.Equals(formatValue, "text", StringComparison.OrdinalIgnoreCase))
                {
                    command.Out.WriteLine(sum.ToString(CultureInfo.InvariantCulture));
                    return 0;
                }

                command.Error.WriteLine($"Unknown format: {formatValue}");
                return 1;
            });
        });

        app.OnExecute(() =>
        {
            app.ShowHelp();
            return 0;
        });

        return app;
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        var app = CommandLineDemoApp.Build();
        return app.Execute(args);
    }
}
