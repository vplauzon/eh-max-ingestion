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
        private const int PAYLOAD_MAX_SIZE = 1000000;
        private const long EVENT_COUNT_REPORT = 10000;
        private const int GATEWAY_COUNT = 100;
        private const int DRONE_EVENT_MIN_COUNT = 5;
        private const int DRONE_EVENT_MAX_COUNT = 20;
        private static readonly IImmutableList<Gateway> _gateways = Enumerable
            .Range(0, GATEWAY_COUNT)
            .Select(i => new Gateway())
            .ToImmutableArray();
        private static readonly Random _random = new Random();

        private static long _eventCount = 0;

        static async Task Main(string[] args)
        {
            var config = SimulatorConfiguration.FromEnvironmentVariables();

            Console.WriteLine("EH Emitter v1.2");
            DisplayConfig(config);

            var producer = new EventHubProducerClient(config.EventHubConnectionString);
            var cancellationTokenSource = new CancellationTokenSource();
            var networkQueue = new ConcurrentQueue<Task>();
            var tasks = Enumerable.Range(0, config.ThreadCount)
                .Select(i => PushEventsAsync(
                    i == 0,
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

        private static void DisplayConfig(SimulatorConfiguration config)
        {
            Console.WriteLine();
            Console.WriteLine($"Event Hub:  {config.EventHubConnectionString}");
            Console.WriteLine($"Thread count:  {config.ThreadCount}");
            Console.WriteLine($"Network depth queue:  {config.NetworkQueueDepth}");
            Console.WriteLine();
        }

        private static async Task PushEventsAsync(
            bool doDisplayEventCount,
            EventHubProducerClient producer,
            ConcurrentQueue<Task> networkQueue,
            int networkQueueDepth,
            CancellationToken token)
        {
            var eventTextList = new List<string>();
            var lastEventCount = _eventCount;

            using (var stream = new MemoryStream())
            {
                while (!token.IsCancellationRequested)
                {
                    var gatewayEvent = CreateGatewayEvent();
                    var gatewayEventText = JsonSerializer.Serialize(gatewayEvent);

                    if (GetEventBody(eventTextList.Append(gatewayEventText)).Length
                        > PAYLOAD_MAX_SIZE)
                    {
                        await SendEventAsync(
                            producer,
                            networkQueue,
                            networkQueueDepth,
                            GetEventBody(eventTextList));
                        Interlocked.Add(ref _eventCount, eventTextList.Count);
                        if (doDisplayEventCount
                            && (_eventCount / EVENT_COUNT_REPORT) != (lastEventCount / EVENT_COUNT_REPORT))
                        {
                            Console.WriteLine($"Events:  {_eventCount}");
                        }
                        lastEventCount = _eventCount;
                        eventTextList.Clear();
                    }
                    eventTextList.Add(gatewayEventText);
                }
            }
        }

        private static string GetEventBody(IEnumerable<string> eventTextList)
        {
            return string.Join('\n', eventTextList);
        }

        private static async Task SendEventAsync(
            EventHubProducerClient producer,
            ConcurrentQueue<Task> networkQueue,
            int networkQueueDepth,
            string eventBody)
        {
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
            var gateway = _gateways[_random.Next(_gateways.Count)];
            var droneEvents = Enumerable
                .Range(0, _random.Next(DRONE_EVENT_MIN_COUNT, DRONE_EVENT_MAX_COUNT))
                .Select(i => new DroneEvent
                {
                    DroneId = gateway.DroneIds[_random.Next(gateway.DroneIds.Count)]
                })
                .ToImmutableArray();
            var gatewayEvent = new GatewayEvent
            {
                GatewayId = gateway.GatewayId,
                Events = droneEvents
            };

            return gatewayEvent;
        }
    }
}