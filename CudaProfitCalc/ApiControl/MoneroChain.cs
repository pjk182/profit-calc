﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ProfitCalc.ApiControl
{
    class MoneroChain
    {
        [JsonProperty("height")]
        public int Height { get; set; }

        /*[JsonProperty("difficulty")]
        public int Difficulty { get; set; }

        [JsonProperty("tx_count")]
        public int TxCount { get; set; }

        [JsonProperty("total_coins")]
        public double TotalCoins { get; set; }*/
    }

    class MoneroLatestBlock
    {
        /*[JsonProperty("id")]
        public string Id { get; set; }*/

        [JsonProperty("height")]
        public int Height { get; set; }

        /*[JsonProperty("timestamp")]
        public int Timestamp { get; set; }*/

        [JsonProperty("difficulty")]
        public int Difficulty { get; set; }

        [JsonProperty("reward")]
        public double Reward { get; set; }

        /*[JsonProperty("size")]
        public int Size { get; set; }

        [JsonProperty("transactions")]
        public Transaction[] Transactions { get; set; }
        public class Transaction
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("amount")]
            public double Amount { get; set; }
        }*/
    }
}