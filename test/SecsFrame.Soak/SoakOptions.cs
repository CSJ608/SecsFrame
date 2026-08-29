using System.Globalization;

namespace SecsFrame.Soak;

internal sealed record SoakOptions(
    int Seed,
    TimeSpan Duration,
    int MaxCycles,
    string OutputPath)
{
    public const string Usage =
        "Usage: SecsFrame.Soak --seed <int> [--duration-seconds <1..1200>] " +
        "[--max-cycles <1..100000>] [--output <jsonl-path>]";

    public static bool IsHelpRequested(string[] args)
        => args.Any(static item => item is "--help" or "-h");

    public static SoakOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        int? seed = null;
        var durationSeconds = 900;
        var maxCycles = 10_000;
        var outputPath = Path.Combine("artifacts", "soak", "session-soak.jsonl");

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--seed":
                    seed = ReadInteger(args, ref index, "--seed");
                    break;
                case "--duration-seconds":
                    durationSeconds = ReadInteger(args, ref index, "--duration-seconds");
                    break;
                case "--max-cycles":
                    maxCycles = ReadInteger(args, ref index, "--max-cycles");
                    break;
                case "--output":
                    outputPath = ReadValue(args, ref index, "--output");
                    break;
                default:
                    throw new SoakConfigurationException($"Unknown option: {args[index]}");
            }
        }

        ValidateRange(durationSeconds, 1, 1200, "--duration-seconds");
        ValidateRange(maxCycles, 1, 100_000, "--max-cycles");
        if (seed is null)
            throw new SoakConfigurationException("--seed is required for reproducibility.");

        return new SoakOptions(
            seed.Value,
            TimeSpan.FromSeconds(durationSeconds),
            maxCycles,
            Path.GetFullPath(outputPath));
    }

    private static int ReadInteger(
        string[] args,
        ref int index,
        string option)
    {
        var value = ReadValue(args, ref index, option);
        if (!int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed))
        {
            throw new SoakConfigurationException($"{option} must be a 32-bit integer.");
        }
        return parsed;
    }

    private static string ReadValue(
        string[] args,
        ref int index,
        string option)
    {
        index++;
        if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new SoakConfigurationException($"{option} requires a value.");
        return args[index];
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string option)
    {
        if (value < minimum || value > maximum)
        {
            throw new SoakConfigurationException(
                $"{option} must be between {minimum} and {maximum}.");
        }
    }
}
