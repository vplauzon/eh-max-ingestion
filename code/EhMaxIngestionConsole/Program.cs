using System.Collections.Immutable;

namespace EhMaxIngestionConsole
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var config = SimulatorConfiguration.FromEnvironmentVariables();
            var cancellationTokenSource = new CancellationTokenSource();
            var tasks = Enumerable.Range(0, config.ThreadCount)
                .Select(i => PushEventsAsync(cancellationTokenSource.Token))
                .ToImmutableArray();

            AppDomain.CurrentDomain.ProcessExit += (object? sender, EventArgs e) =>
            {
                cancellationTokenSource.Cancel();
            };

            await Task.WhenAll(tasks);
        }

        private static Task PushEventsAsync(CancellationToken token)
        {
            return Task.CompletedTask;
        }
    }
}