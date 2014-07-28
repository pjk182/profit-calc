﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProfitCalc.ApiControl;

namespace ProfitCalc
{
    internal class Coin
    {
        public string FullName { get; set; }
        public string TagName { get; set; }

        public HashAlgo.Algo Algo { get; set; }
        public double Difficulty { get; set; }
        public double BlockReward { get; set; }
        public uint Height { get; set; }

        public List<Exchange> Exchanges { get; set; }

        internal class Exchange
        {
            public string ExchangeName { get; set; }
            public double BtcPrice { get; set; }
            public double BtcVolume { get; set; }
            public double Weight { get; set; }
            public bool IsFrozen { get; set; }

            public override string ToString()
            {
                return ExchangeName + " | BTC Price: " + BtcPrice + " | BTC Volume: " + BtcVolume;
            }
        }

        public double WeightedBtcPrice { get; set; }
        public double TotalVolume { get; set; }


        public double CoinsPerDay { get; set; }
        public double BtcPerDay { get; set; }

        public double UsdPerDay { get; set; }
        public double EurPerDay { get; set; }
        public double GbpPerDay { get; set; }
        public double CnyPerDay { get; set; }

        public bool HasFrozenMarkets { get; set; }
        public bool HasImplementedMarketApi { get; set; }
        public bool IsMultiPool;

        public Coin()
        {
            Exchanges = new List<Exchange>();
        }

        public Coin(NiceHash.Result.Stat niceHashStat)
        {
            switch (niceHashStat.Algo)
            {
                case 0:
                    Algo = HashAlgo.Algo.Scrypt;
                    break;
                case 2:
                    Algo = HashAlgo.Algo.ScryptN;
                    break;
                case 3:
                    Algo = HashAlgo.Algo.X11;
                    break;
                case 4:
                    Algo = HashAlgo.Algo.X13;
                    break;
                case 5:
                    Algo = HashAlgo.Algo.Keccak;
                    break;
                case 6:
                    Algo = HashAlgo.Algo.X15;
                    break;
                case 7:
                    Algo = HashAlgo.Algo.Nist5;
                    break;
                default:
                    Algo = HashAlgo.Algo.Unknown;
                    break;
            }

            FullName = "Act. NiceHash " + Algo;
            TagName = "NICE" + Algo.ToString().ToUpper();
            Difficulty = 0;
            BlockReward = 0;

            Exchange nhExchange = new Exchange
            {
                ExchangeName = "NiceHash",
                BtcPrice = niceHashStat.Price,
                BtcVolume = 0,
                Weight = 1,
                IsFrozen = false
            };
            Exchanges = new List<Exchange> {nhExchange};
            TotalVolume = 0;
            IsMultiPool = true;
            HasImplementedMarketApi = true;
        }

        public Coin(KeyValuePair<string, WhatToMine.Coin> wtmCoin)
        {
            FullName = wtmCoin.Key;
            TagName = wtmCoin.Value.Tag.ToUpper();
            Algo = HashAlgo.GetAlgorithm(wtmCoin.Value.Algorithm);
            if (TagName == "MYR" && Algo == HashAlgo.Algo.Groestl) Algo = HashAlgo.Algo.MyriadGroestl;
            Difficulty = wtmCoin.Value.Difficulty;
            BlockReward = wtmCoin.Value.BlockReward;
            Height = wtmCoin.Value.LastBlock;
            Exchange wtmExchange = new Exchange
            {
                ExchangeName = "Unknown (WhatToMine)",
                BtcPrice = wtmCoin.Value.ExchangeRate,
                BtcVolume = wtmCoin.Value.Volume*wtmCoin.Value.ExchangeRate,
                Weight = 1,
                IsFrozen = false
            };
            Exchanges = new List<Exchange> {wtmExchange};
            TotalVolume = wtmExchange.BtcVolume;
            IsMultiPool = false;
            HasImplementedMarketApi = false;
        }

        public Coin(CoinTweak.Coin ctwCoin)
        {
            FullName = ctwCoin.CoinFullname;
            TagName = ctwCoin.CoinName.ToUpper();
            Algo = HashAlgo.GetAlgorithm(ctwCoin.AlgoName);
            if (TagName == "MYR" && Algo == HashAlgo.Algo.Groestl) Algo = HashAlgo.Algo.MyriadGroestl;
            Difficulty = ctwCoin.Difficulty;
            BlockReward = ctwCoin.BlockCoins;
            Exchange ctwExchange = new Exchange
            {
                ExchangeName = ctwCoin.ExName + " (CoinTweak)",
                BtcPrice = ctwCoin.ConversionRateToBtc,
                BtcVolume = ctwCoin.BtcVol,
                Weight = 1,
                IsFrozen = !ctwCoin.HasBuyOffers
            };
            Exchanges = new List<Exchange> {ctwExchange};
            TotalVolume = ctwExchange.BtcVolume;
            IsMultiPool = false;
            HasImplementedMarketApi = false;
        }

        public Coin(CoinWarz.Coin cwzCoin)
        {
            FullName = cwzCoin.CoinName;
            TagName = cwzCoin.CoinTag.ToUpper();
            Algo = HashAlgo.GetAlgorithm(cwzCoin.Algorithm);
            if (TagName == "MYR" && Algo == HashAlgo.Algo.Groestl) Algo = HashAlgo.Algo.MyriadGroestl;
            Difficulty = cwzCoin.Difficulty;
            BlockReward = cwzCoin.BlockReward;
            Height = cwzCoin.BlockCount;
            Exchange cwzExchange = new Exchange
            {
                ExchangeName = cwzCoin.Exchange + " (CoinWarz)",
                BtcPrice = cwzCoin.ExchangeRate,
                BtcVolume = cwzCoin.ExchangeVolume*cwzCoin.ExchangeRate,
                Weight = 1,
                IsFrozen = cwzCoin.HealthStatus.Contains("Unhealthy")
            };
            Exchanges = new List<Exchange> {cwzExchange};
            TotalVolume = cwzExchange.BtcVolume;
            IsMultiPool = false;
            HasImplementedMarketApi = false;
        }

        public void CalcProfitability(double hashRateMh, bool useWeightedCalculations, int multiplier)
        {
            if (IsMultiPool)
            {
                CoinsPerDay = 0;
                BtcPerDay = ((hashRateMh/1000)*Exchanges[0].BtcPrice) * multiplier;
            }
            else
            {
                SortExchanges();

                switch (Algo)
                {
                    case HashAlgo.Algo.CryptoNight:
                        //Cryptonight's difficulty is net hashrate * 60
                        CoinsPerDay = (BlockReward*24*60)*((hashRateMh*1000000)
                                                    /(Difficulty/60))*multiplier;
                        break;
                    case HashAlgo.Algo.Quark:
                        CoinsPerDay = (BlockReward/(Difficulty*Math.Pow(2, 24)
                                                    /(hashRateMh*1000000)/3600/24))*multiplier;
                        break;
                    default:
                        CoinsPerDay = (BlockReward/(Difficulty*Math.Pow(2, 32)
                                                    /(hashRateMh*1000000)/3600/24))*multiplier;
                        break;
                }

                foreach (Exchange exchange in Exchanges)
                {
                    exchange.Weight = exchange.BtcVolume/TotalVolume;
                    WeightedBtcPrice += (exchange.Weight*exchange.BtcPrice);
                }

                BtcPerDay = !useWeightedCalculations ? CoinsPerDay*Exchanges[0].BtcPrice : CoinsPerDay*WeightedBtcPrice;
            }
        }

        public void SortExchanges()
        {
            Exchanges = Exchanges.OrderByDescending(exchange => exchange.BtcVolume).ToList();
        }

        public override string ToString()
        {
            return "TAG: " + TagName + " | Name:" + FullName + " | Algo: " + Algo + " | BTC/day: " +
                   BtcPerDay.ToString("#.00000000") + " | Coins/day: " + CoinsPerDay.ToString("#.00000000") + 
                   GetExchanges() + 
                   " | Weighted price: " + WeightedBtcPrice.ToString("#.00000000") + " | Total volume: " +
                   TotalVolume.ToString("#.0000") + " | Difficulty: " + Difficulty.ToString("#.###") + 
                   " | Blockreward: " + BlockReward.ToString("#.###");
        }

        public string GetExchanges()
        {
            StringBuilder sb = new StringBuilder();
            for (int index = 0; index < Exchanges.Count; index++)
            {
                sb.Append(" | Exchange #").Append(index + 1).Append(": ").Append(Exchanges[index].ExchangeName)
                    .Append(" | BTC volume: ").Append(Exchanges[index].BtcVolume.ToString("#.0000"))
                    .Append(" | BTC price: ").Append(Exchanges[index].BtcPrice.ToString("#.00000000"))
                    .Append(" | Weight: ").Append(Exchanges[index].Weight.ToString("#.000"));
            }

            return sb.ToString();
        }
    }

    internal class CustomCoin
    {
        public string Tag { get; set; }
        public string FullName { get; set; }

        public HashAlgo.Algo Algo { get; set; }
        public double Difficulty { get; set; }
        public double BlockReward { get; set; }
    }
}