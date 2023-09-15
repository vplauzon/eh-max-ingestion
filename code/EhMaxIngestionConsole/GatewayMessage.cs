using System;

namespace EhMaxIngestionConsole
{
    public class GatewayMessage
    {
        public string GatewayId { get; set; } = string.Empty;

        public string MessageId { get; set; } = Guid.NewGuid().ToString();
        
        public DateTime MessageTimestamp { get; set; } = DateTime.Now;

        public DroneEvent[] Events { get; set; } = new DroneEvent[0];
    }
}