﻿using System.Collections.Generic;
using Newtonsoft.Json;

namespace ProfitCalc.ApiControl.TemplateClasses
{
    internal class BittrexPairs
    {
        /*[JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }*/

        [JsonProperty("result")]
        public List<Result> Results { get; set; }

        public class Result
        {
            [JsonProperty("MarketName")]
            public string MarketName { get; set; }

            /*[JsonProperty("High")]
            public string High { get; set; }

            [JsonProperty("Low")]
            public string Low { get; set; }

            [JsonProperty("Volume")]
            public string Volume { get; set; }*/

            [JsonProperty("Last")]
            public double Last { get; set; }

            [JsonProperty("BaseVolume")]
            public double BaseVolume { get; set; }

            /*[JsonProperty("TimeStamp")]
            public string TimeStamp { get; set; }*/

            [JsonProperty("Bid")]
            public double Bid { get; set; }

            [JsonProperty("Ask")]
            public double Ask { get; set; }

            [JsonProperty("OpenBuyOrders")]
            public string OpenBuyOrders { get; set; }

            [JsonProperty("OpenSellOrders")]
            public string OpenSellOrders { get; set; }

            /*[JsonProperty("PrevDay")]
            public string PrevDay { get; set; }

            [JsonProperty("Created")]
            public string Created { get; set; }*/
        }
    }

    internal class BittrexOrders
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        /*[JsonProperty("message")]
        public string Message { get; set; }*/

        [JsonProperty("result")]
        public Results Result { get; set; }
        internal class Results
        {

            [JsonProperty("buy")]
            public Order[] Buy { get; set; }

            [JsonProperty("sell")]
            public Order[] Sell { get; set; }
            internal class Order
            {
                [JsonProperty("Quantity")]
                public double Quantity { get; set; }

                [JsonProperty("Rate")]
                public double Rate { get; set; }
            }
        }
    }
}