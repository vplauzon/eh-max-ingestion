using System;

namespace EhMaxIngestionConsole
{
    public class DroneEvent
    {
        private static readonly Random _random = new Random();

        public string DroneId { get; set; } = string.Empty;

        public DateTime EventTimestamp { get; set; } = DateTime.Now;
        
        public string Device { get; set; } = string.Empty;

        public double Measurement { get; set; } = _random.NextDouble() * 100;
    }
}