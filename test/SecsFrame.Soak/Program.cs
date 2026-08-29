namespace SecsFrame.Soak;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (SoakOptions.IsHelpRequested(args))
        {
            Console.WriteLine(SoakOptions.Usage);
            return 0;
        }

        try
        {
            var options = SoakOptions.Parse(args);
            var report = SoakReportWriter.Create(options.OutputPath);
            await using var reportScope = report.ConfigureAwait(false);
            await new HsmsSoakRunner(options, report).RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (SoakConfigurationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(SoakOptions.Usage);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
