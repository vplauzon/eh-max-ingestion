using System;

namespace EhMaxIngestionConsole
{
    internal class SimulatorConfiguration
    {
        #region Constructor
        private SimulatorConfiguration(
            string eventHubConnectionString,
            int threadCount,
            int networkQueueDepth)
        {
            EventHubConnectionString = eventHubConnectionString;
            ThreadCount = threadCount;
            NetworkQueueDepth = networkQueueDepth;
        }

        public static SimulatorConfiguration FromEnvironmentVariables()
        {
            var eventHubConnectionString = GetConfigurationString("EVENT_HUB_CONN_STRING");
            var threadCount = GetConfigurationInteger("THREAD_COUNT", 1);
            var networkQueueDepth = GetConfigurationInteger("NETWORK_QUEUE_DEPTH", 1);

            return new SimulatorConfiguration(
                eventHubConnectionString,
                threadCount,
                networkQueueDepth);
        }
        #endregion

        public string EventHubConnectionString { get; }

        public int ThreadCount { get; }

        public int NetworkQueueDepth { get; }

        #region Configuration key methods
        private static string? GetConfigurationStringInternal(string key, bool isNullOk)
        {
            var value = Environment.GetEnvironmentVariable(key);

            if (string.IsNullOrWhiteSpace(value) && !isNullOk)
            {
                throw new ArgumentNullException("Environment variable missing", key);
            }
            else
            {
                return value;
            }
        }

        private static string GetConfigurationString(string key, string? defaultValue = null)
        {
            var value = GetConfigurationStringInternal(key, defaultValue == null);

            if (defaultValue != null)
            {
                return value ?? defaultValue;
            }
            else
            {
                return value!;
            }
        }

        private static int GetConfigurationInteger(string key, int? defaultValue = null)
        {
            var text = GetConfigurationStringInternal(key, defaultValue == null);

            if (text != null)
            {
                int value;

                if (!int.TryParse(text, out value))
                {
                    throw new ArgumentException("Env Var isn't an integer", key);
                }
                else
                {
                    return value;
                }
            }
            else
            {
                return defaultValue!.Value;
            }
        }
        #endregion
    }
}