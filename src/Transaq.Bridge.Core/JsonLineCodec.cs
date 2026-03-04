using System.Text;
using Newtonsoft.Json;

namespace Transaq.Bridge.Core
{
    public static class JsonLineCodec
    {
        public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string Serialize(Envelope envelope)
        {
            return JsonConvert.SerializeObject(envelope) + "\n";
        }

        public static Envelope Deserialize(string line)
        {
            return JsonConvert.DeserializeObject<Envelope>(line);
        }
    }
}
