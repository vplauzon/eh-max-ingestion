using System;
using System.Collections.Immutable;

namespace EhMaxIngestionConsole
{
    public class DroneEvent
    {
        private static readonly Random _random = new Random();
        private static readonly IImmutableList<string> _deviceList = new[]
        {
            "Temperature",
            "Pressure",
            "Light",
            "Humidity",
            "Smoke",
            "Speed",
            "Acceleration"
        }.ToImmutableArray();

        public string DroneId { get; set; } = string.Empty;

        public DateTime EventTimestamp { get; set; } = DateTime.Now;

        public string Device { get; set; } = _deviceList[_random.Next(_deviceList.Count)];

        public double Measurement { get; set; } = _random.NextDouble() * 100;
    }
}