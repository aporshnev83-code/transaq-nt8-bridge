using System.Collections.Generic;

namespace TransaqGateway
{
    public class AppConfig
    {
        public string Login { get; set; }
        public string Password { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public bool AutoPos { get; set; }
        public string LogDir { get; set; }
        public string PipeName { get; set; }
        public List<InstrumentConfig> Instruments { get; set; }
    }

    public class InstrumentConfig
    {
        public string Board { get; set; }
        public string Seccode { get; set; }
    }
}
