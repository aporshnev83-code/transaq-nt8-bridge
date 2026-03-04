using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Transaq.Bridge.Core
{
    public class Envelope
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("tsUtc")]
        public string TsUtc { get; set; }

        [JsonProperty("payload")]
        public JObject Payload { get; set; }

        public static Envelope Create(string type, object payload)
        {
            return new Envelope
            {
                Type = type,
                TsUtc = DateTime.UtcNow.ToString("o"),
                Payload = payload == null ? new JObject() : JObject.FromObject(payload)
            };
        }
    }

    public class InstrumentKey
    {
        public string Board { get; set; }
        public string SecCode { get; set; }

        public override string ToString()
        {
            return (Board ?? string.Empty) + ":" + (SecCode ?? string.Empty);
        }
    }

    public class MarketDataSnapshot
    {
        public InstrumentKey Instrument { get; set; }
        public decimal? Bid { get; set; }
        public decimal? Ask { get; set; }
        public decimal? Last { get; set; }
    }

    public class DomLevel
    {
        public decimal Price { get; set; }
        public decimal BidSize { get; set; }
        public decimal AskSize { get; set; }
    }

    public class DomSnapshot
    {
        public InstrumentKey Instrument { get; set; }
        public List<DomLevel> Levels { get; set; }
    }

    public class NewOrderCommand
    {
        public string ClientOrderId { get; set; }
        public InstrumentKey Instrument { get; set; }
        public string Side { get; set; }
        public string OrderType { get; set; }
        public decimal Quantity { get; set; }
        public decimal? Price { get; set; }
    }

    public class CancelOrderCommand
    {
        public string ClientOrderId { get; set; }
        public long? TransactionId { get; set; }
    }

    public class OrderUpdate
    {
        public string ClientOrderId { get; set; }
        public long? TransactionId { get; set; }
        public string OrderNo { get; set; }
        public string State { get; set; }
        public decimal Filled { get; set; }
        public string Message { get; set; }
    }

    public class PositionUpdate
    {
        public InstrumentKey Instrument { get; set; }
        public decimal Quantity { get; set; }
        public decimal AvgPrice { get; set; }
    }
}
