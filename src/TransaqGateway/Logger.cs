using System;
using System.IO;

namespace TransaqGateway
{
    public class Logger
    {
        private readonly object _sync = new object();
        private readonly string _logFile;

        public Logger(string logDir)
        {
            Directory.CreateDirectory(logDir);
            _logFile = Path.Combine(logDir, "gateway.log");
        }

        public void Info(string message)
        {
            Write("INFO", message);
        }

        public void Error(string message)
        {
            Write("ERROR", message);
        }

        private void Write(string level, string message)
        {
            var line = string.Format("{0:o} [{1}] {2}", DateTime.UtcNow, level, message);
            lock (_sync)
            {
                File.AppendAllText(_logFile, line + Environment.NewLine);
            }
            Console.WriteLine(line);
        }
    }
}
