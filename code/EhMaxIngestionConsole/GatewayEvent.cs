using System;
using System.Collections.Immutable;

namespace EhMaxIngestionConsole
{
    public class GatewayEvent
    {
        public string GatewayId { get; set; } = string.Empty;

        public string MessageId { get; set; } = Guid.NewGuid().ToString();
        
        public DateTime MessageTimestamp { get; set; } = DateTime.Now;

        public IImmutableList<DroneEvent> Events { get; set; } = ImmutableArray<DroneEvent>.Empty;
    }
}