using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EhMaxIngestionConsole
{
    internal class Gateway
    {
        private const int DRONE_COUNT = 10;

        public string GatewayId { get; } = Guid.NewGuid().ToString();

        public IImmutableList<string> DroneIds { get; } = Enumerable
            .Range(0, DRONE_COUNT)
            .Select(i => Guid.NewGuid().ToString())
            .ToImmutableArray();
    }
}