using System;
using System.IO;
using Newtonsoft.Json;

namespace TransaqGateway
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var configPath = args.Length > 0 ? args[0] : "config.json";
            if (!File.Exists(configPath))
            {
                Console.WriteLine("Config not found: " + configPath);
                return;
            }

            var config = JsonConvert.DeserializeObject<AppConfig>(File.ReadAllText(configPath));
            var logDir = string.IsNullOrWhiteSpace(config.LogDir) ? "logs" : config.LogDir;
            var logger = new Logger(logDir);
            logger.Info("Starting TransaqGateway with pipe " + (config.PipeName ?? "transaq-nt8"));

            var service = new GatewayService(config, logger);
            service.Start();

            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                service.Stop();
                logger.Info("Stopping by Ctrl+C");
            };

            Console.WriteLine("Gateway running. Press Enter to stop.");
            Console.ReadLine();
            service.Stop();
        }
    }
}
