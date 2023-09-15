using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;

namespace EhMaxIngestionConsole
{
    internal class Program
    {
        private const int PAYLOAD_MAX_SIZE = 1048576;
        private const long EVENT_COUNT_REPORT = 10;
        private const int GATEWAY_COUNT = 200;
        private const int DRONE_COUNT = 1000;
        private const int DRONE_EVENT_MIN_COUNT = 5;
        private const int DRONE_EVENT_MAX_COUNT = 20;
        private static readonly IImmutableList<string> _gatewayIds = Enumerable
            .Range(0, GATEWAY_COUNT)
            .Select(i => Guid.NewGuid().ToString())
            .ToImmutableArray();
        private static readonly IImmutableList<string> _droneIds = Enumerable
            .Range(0, DRONE_COUNT)
            .Select(i => Guid.NewGuid().ToString())
            .ToImmutableArray();
        private static readonly Random _random = new Random();

        static async Task Main(string[] args)
        {
            var config = SimulatorConfiguration.FromEnvironmentVariables();
            var producer = new EventHubProducerClient(config.EventHubConnectionString);
            var cancellationTokenSource = new CancellationTokenSource();
            var networkQueue = new ConcurrentQueue<Task>();
            var tasks = Enumerable.Range(0, config.ThreadCount)
                .Select(i => PushEventsAsync(
                    producer,
                    networkQueue,
                    config.NetworkQueueDepth,
                    cancellationTokenSource.Token))
                .ToImmutableArray();

            AppDomain.CurrentDomain.ProcessExit += (object? sender, EventArgs e) =>
            {
                cancellationTokenSource.Cancel();
            };

            await Task.WhenAll(tasks);
        }

        private static async Task PushEventsAsync(
            EventHubProducerClient producer,
            ConcurrentQueue<Task> networkQueue,
            int networkQueueDepth,
            CancellationToken token)
        {
            var eventTextList = new List<string>();
            var totalEventSize = 0;
            var eventCount = (long)0;

            using (var stream = new MemoryStream())
            {
                while (!token.IsCancellationRequested)
                {
                    var gatewayEvent = CreateGatewayEvent();
                    var gatewayEventText = JsonSerializer.Serialize(gatewayEvent);

                    if (totalEventSize + 1 + gatewayEventText.Length > PAYLOAD_MAX_SIZE)
                    {
                        await SendEventAsync(
                            producer,
                            networkQueue,
                            networkQueueDepth,
                            eventTextList);
                        eventTextList.Clear();
                        totalEventSize = 0;
                        ++eventCount;
                        if (eventCount % EVENT_COUNT_REPORT == 0)
                        {
                            Console.WriteLine($"Events:  {eventCount}");
                        }
                    }
                    totalEventSize += gatewayEventText.Length;
                    totalEventSize += eventTextList.Any() ? 1 : 0;
                    eventTextList.Add(gatewayEventText);
                }
            }
        }

        private static async Task SendEventAsync(
            EventHubProducerClient producer,
            ConcurrentQueue<Task> networkQueue,
            int networkQueueDepth,
            IEnumerable<string> eventTextList)
        {
            var eventBody = string.Join('\n', eventTextList);
            var eventData = new EventData(eventBody);

            while (networkQueue.Count() > networkQueueDepth)
            {
                if (networkQueue.TryPeek(out var task))
                {
                    if (!task.IsCompleted)
                    {   //  Await the send at the bottom of the queue
                        await task;
                    }
                    else
                    {
                        if (networkQueue.TryDequeue(out var otherTask))
                        {   //  Await in case we picked another task
                            await otherTask;
                        }
                    }
                }
            }

            var newTask = producer.SendAsync(new[] { eventData });

            networkQueue.Enqueue(newTask);
        }

        private static GatewayEvent CreateGatewayEvent()
        {
            var droneEvents = Enumerable
                .Range(0, _random.Next(DRONE_EVENT_MIN_COUNT, DRONE_EVENT_MAX_COUNT))
                .Select(i => new DroneEvent
                {
                    DroneId = _droneIds[_random.Next(_droneIds.Count)]
                })
                .ToImmutableArray();
            var gatewayEvent = new GatewayEvent
            {
                GatewayId = _gatewayIds[_random.Next(_gatewayIds.Count)],
                Events = droneEvents
            };

            return gatewayEvent;
        }
    }
}