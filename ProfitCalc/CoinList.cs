﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ProfitCalc.ApiControl;
using ProfitCalc.ApiControl.TemplateClasses;

namespace ProfitCalc
{
    internal class CoinList
    {
        public List<Coin> ListOfCoins { get; set; }
        private readonly HttpClient _client;
        private readonly ParallelOptions _po;
        public Profile UsedProfile { get; set; }
        private readonly int _bidRecentAsk;
        private readonly bool _getOrderDepth;

        public CoinList(HttpClient client, Profile profile, int index, bool getOrderDepth)
        {
            ListOfCoins = new List<Coin>();
            _client = client;
            UsedProfile = profile;

            // 0 == highest bid price, 1 == recent trade price, 2 = lowest ask price
            _bidRecentAsk = index;
            _getOrderDepth = getOrderDepth;

            _po = new ParallelOptions
            {
                CancellationToken = new CancellationTokenSource().Token,
                MaxDegreeOfParallelism = Environment.ProcessorCount * 4,
            };
        }

        public void Add(Coin newCoin)
        {
            bool found = false;
            foreach (Coin c in ListOfCoins)
            {
                if (c.TagName == newCoin.TagName && c.Algo == newCoin.Algo && !newCoin.IsMultiPool)
                {
                    if (c.Height < newCoin.Height)
                    {
                        ListOfCoins.Remove(c);
                    }
                    else
                    {
                        found = true;
                    }

                    break;
                }
            }

            if (!found && UsedInProfile(newCoin.Algo, UsedProfile.CustomAlgoList))
            {
                ListOfCoins.Add(newCoin);
            }
        }

        private bool UsedInProfile(string algo, IEnumerable<CustomAlgo> customAlgoList)
        {
            bool used = false;

            Parallel.ForEach(customAlgoList, _po, savedAlgo =>
            {
                if (savedAlgo.Name == algo && savedAlgo.Use)
                {
                    used = true;
                    _po.CancellationToken.ThrowIfCancellationRequested();
                }
            });

            return used;
        }

        public void AddCustomCoins(IEnumerable<CustomCoin> customCoins)
        {
            int errorCount = 0;
            foreach (CustomCoin customCoin in customCoins)
            {
                if (customCoin.Use)
                {
                    if (customCoin.UseRpc)
                    {
                        try
                        {
                            BitnetClient bc = new BitnetClient(
                                "http://" + customCoin.RpcIp + ":" + customCoin.RpcPort + "/")
                            {
                                Credentials = new NetworkCredential(
                                    customCoin.RpcUser, customCoin.RpcPass)
                            };
                            JObject info = bc.GetMiningInfo();
                            JToken height;
                            if (info.TryGetValue("blocks", out height))
                            {
                                customCoin.Height = height.Value<uint>();
                            }

                            if (customCoin.GetDiff)
                            {
                                JToken diff;
                                if (info.TryGetValue("difficulty", out diff))
                                {
                                    customCoin.Difficulty = diff.Value<float>();
                                }
                            }

                            if (customCoin.GetReward)
                            {
                                JToken reward;
                                if (info.TryGetValue("reward", out reward))
                                {
                                    customCoin.BlockReward = reward.Value<float>();
                                }
                            }

                            if (customCoin.GetNetHash)
                            {
                                JToken networkHashPs;
                                if (info.TryGetValue("networkhashps", out networkHashPs))
                                {
                                    customCoin.NetHashRate = networkHashPs.Value<float>()/1000000;
                                }
                            }
                        }
                        catch
                        {
                            errorCount++;
                        }
                    }

                    Add(new Coin(customCoin));
                }
            }

            if (errorCount > 0)
            {
                throw new Exception("Cannot connect with one or more daemons/wallet-qts");
            }
        }

        private string GetCleanedAlgo(string algo)
        {
            string cleanAlgo = algo.Trim();
            Parallel.ForEach(UsedProfile.CustomAlgoList, _po, savedAlgo =>
            {
                if (savedAlgo.Name == cleanAlgo)
                {
                    _po.CancellationToken.ThrowIfCancellationRequested();
                }
                else if (!string.IsNullOrWhiteSpace(savedAlgo.SynonymsCsv))
                {
                    string[] synonyms = savedAlgo.SynonymsCsv.Split(',');
                    Parallel.ForEach(synonyms, synonym =>
                    {
                        if (synonym == cleanAlgo)
                        {
                            cleanAlgo = savedAlgo.Name;
                            _po.CancellationToken.ThrowIfCancellationRequested();
                        }
                    });
                }
            });

            return cleanAlgo;
        }

        public void UpdateNiceHash()
        {
            NiceHash niceHashData = JsonControl.DownloadSerializedApi<NiceHash>(
                _client.GetStreamAsync("http://www.nicehash.com/api?method=stats.global.current").Result);
            foreach (NiceHash.Result.Stat nhResultStat in niceHashData.Results.Stats)
            {
                Add(new Coin(nhResultStat, "NiceHash"));
            }

            /*NiceHash westHashData = JsonControl.DownloadSerializedApi<NiceHash>(
                _client.GetStreamAsync("http://www.westhash.com/api?method=stats.global.current").Result);

            for (int i = 0; i < niceHashData.Results.Stats.Count; i++)
            {
                    Add(new Coin(niceHashData.Results.Stats[i], "NiceHash"));
                    Add(new Coin(westHashData.Results.Stats[i], "WestHash"));
            }*/
        }

        public void UpdateWhatToMine()
        {
            WhatToMine wtmData = JsonControl.DownloadSerializedApi<WhatToMine>(_client.GetStreamAsync("http://www.whattomine.com/coins.json").Result);
            foreach (KeyValuePair<string, WhatToMine.Coin> wtmCoin in wtmData.Coins)
            {
                Coin c = new Coin(wtmCoin);
                c.Algo = GetCleanedAlgo(c.Algo);
                Add(c);
            }
        }

        public void UpdateCoinTweak(string apiKey)
        {
            CoinTweak ctwData = JsonControl.DownloadSerializedApi<CoinTweak>(
                _client.GetStreamAsync("http://cointweak.com/API/getProfitOverview/&key=" + apiKey).Result);

            if (ctwData.Success)
            {
                foreach (CoinTweak.Coin ctwCoin in ctwData.Coins)
                {
                    Coin c = new Coin(ctwCoin);
                    if (c.TagName == "RUBY")
                    {
                        c.TagName = "RBY";
                    }
                    c.Algo = GetCleanedAlgo(c.Algo);

                    Add(c);
                }
            }
            else
            {
                throw new Exception(ctwData.CallsRemaining.ToString(CultureInfo.InvariantCulture) + " calls remaining or invalid API key");
            }
        }

        public void UpdateCoinWarz(string apiKey)
        {
            CoinWarz cwzData = JsonControl.DownloadSerializedApi<CoinWarz>(
                _client.GetStreamAsync("http://www.coinwarz.com/v1/api/profitability/?algo=all&apikey=" + apiKey).Result);

            if (cwzData.Success)
            {
                foreach (CoinWarz.Coin cwzCoin in cwzData.Data)
                {
                    Coin c = new Coin(cwzCoin);
                    c.Algo = GetCleanedAlgo(c.Algo);
                    Add(c);
                }
            }
            else
            {
                throw new Exception(cwzData.Message);
            }
        }

        public void UpdateMintPal()
        {
            MintPalPairs mp = JsonControl.DownloadSerializedApi<MintPalPairs>(
                _client.GetStreamAsync("https://api.mintpal.com/v2/market/summary/BTC").Result);

            Parallel.ForEach(ListOfCoins, c => Parallel.ForEach(mp.Data, _po, mpCoin =>
            {
                if (mpCoin.Exchange == "BTC" && mpCoin.Code == c.TagName)
                {
                    double priceToUse;
                    switch (_bidRecentAsk)
                    {
                        case 0:
                            priceToUse = mpCoin.TopBid;
                            break;
                        case 1:
                            priceToUse = mpCoin.LastPrice;
                            break;
                        case 2:
                            priceToUse = mpCoin.TopAsk;
                            break;
                        default:
                            priceToUse = mpCoin.TopBid;
                            break;
                    }

                    Coin.Exchange mpExchange = new Coin.Exchange
                    {
                        ExchangeName = "MintPal",
                        BtcPrice = priceToUse,
                        BtcVolume = mpCoin.Last24HVol,
                        BuyOrders = new List<Coin.Exchange.Order>(),
                        SellOrders = new List<Coin.Exchange.Order>()
                    };

                    if (_getOrderDepth)
                    {
                        MintPalOrders mpOrders = JsonControl.DownloadSerializedApi<MintPalOrders>(
                            _client.GetStreamAsync("https://api.mintpal.com/v2/market/orders/"
                            + mpCoin.Code + "/BTC/ALL").Result);
                        foreach (MintPalOrders.Datas data in mpOrders.Data)
                        {
                            foreach (MintPalOrders.Datas.Order newOrder in data.Orders)
                            {
                                double price, volume, coinVolume;
                                if (Double.TryParse(newOrder.Price, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out price) &&
                                    Double.TryParse(newOrder.Total, NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out volume) &&
                                    Double.TryParse(newOrder.Amount, NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out coinVolume))
                                {
                                    Coin.Exchange.Order order = new Coin.Exchange.Order
                                    {
                                        BtcPrice = price,
                                        BtcVolume = volume,
                                        CoinVolume = coinVolume
                                    };

                                    switch (data.Type)
                                    {
                                        case "buy":
                                            mpExchange.BuyOrders.Add(order);
                                            break;
                                        case "sell":
                                            mpExchange.SellOrders.Add(order);
                                            break;
                                    }
                                }
                            }
                        }
                    }
                    
                    if (c.HasImplementedMarketApi)
                    {
                        c.Exchanges.Add(mpExchange);
                        c.TotalVolume += mpExchange.BtcVolume;
                    }
                    else
                    {
                        c.Exchanges = new List<Coin.Exchange> {mpExchange};
                        c.TotalVolume = mpExchange.BtcVolume;
                        c.HasImplementedMarketApi = true;
                    }

                    _po.CancellationToken.ThrowIfCancellationRequested();
                }
            }));
        }

        public void UpdateCryptsy()
        {
            Cryptsy cp = JsonControl.DownloadSerializedApi<Cryptsy>(
                _client.GetStreamAsync("http://pubapi.cryptsy.com/api.php?method=marketdatav2").Result);

            Parallel.ForEach(ListOfCoins, c => Parallel.ForEach(cp.Returns.Markets, _po, cpCoin =>
            {
                if (cpCoin.Value.SecondaryCode == "BTC" && ((cpCoin.Value.PrimaryCode == c.TagName) 
                    || (cpCoin.Value.PrimaryCode == "STR" && c.TagName == "STAR")))
                {
                    double priceToUse;
                    switch (_bidRecentAsk)
                    {
                        case 0:
                            priceToUse = cpCoin.Value.BuyOrders != null
                                && cpCoin.Value.BuyOrders.Any()
                                ? cpCoin.Value.BuyOrders[0].Price
                                : cpCoin.Value.LastTradePrice;
                            break;
                        case 1:
                            priceToUse = cpCoin.Value.LastTradePrice;
                            break;
                        case 2:
                            priceToUse = cpCoin.Value.SellOrders != null
                                && cpCoin.Value.SellOrders.Any()
                                ? cpCoin.Value.SellOrders[0].Price
                                : cpCoin.Value.LastTradePrice;
                            break;
                        default:
                            priceToUse = cpCoin.Value.BuyOrders != null
                                && cpCoin.Value.BuyOrders.Any()
                                ? cpCoin.Value.BuyOrders[0].Price
                                : cpCoin.Value.LastTradePrice;
                            break;
                    }

                    Coin.Exchange cpExchange = new Coin.Exchange
                    {
                        ExchangeName = "Cryptsy",
                        BtcPrice = priceToUse,
                        BtcVolume = (cpCoin.Value.Volume*priceToUse),
                        BuyOrders = new List<Coin.Exchange.Order>(),
                        SellOrders = new List<Coin.Exchange.Order>()
                    };

                    if (_getOrderDepth)
                    {
                        if (cpCoin.Value.BuyOrders != null && cpCoin.Value.BuyOrders.Any())
                        {
                            int i = cpCoin.Value.BuyOrders.Length < 50
                                ? cpCoin.Value.BuyOrders.Length : 50;
                            for (int index = 0; index < i; index++)
                            {
                                Coin.Exchange.Order order = new Coin.Exchange.Order
                                {
                                    BtcPrice = cpCoin.Value.BuyOrders[index].Price,
                                    BtcVolume = cpCoin.Value.BuyOrders[index].Total,
                                    CoinVolume = cpCoin.Value.BuyOrders[index].Quantity
                                };
                                cpExchange.BuyOrders.Add(order);
                            }
                        }

                        if (cpCoin.Value.SellOrders != null && cpCoin.Value.SellOrders.Any())
                        {
                            int i = cpCoin.Value.SellOrders.Length < 50
                                ? cpCoin.Value.SellOrders.Length : 50;
                            for (int index = 0; index < i; index++)
                            {
                                Coin.Exchange.Order order = new Coin.Exchange.Order
                                {
                                    BtcPrice = cpCoin.Value.SellOrders[index].Price,
                                    BtcVolume = cpCoin.Value.SellOrders[index].Total,
                                    CoinVolume = cpCoin.Value.SellOrders[index].Quantity
                                };
                                cpExchange.SellOrders.Add(order);
                            }
                        }
                    }
                    
                    if (c.HasImplementedMarketApi)
                    {
                        c.Exchanges.Add(cpExchange);
                        c.TotalVolume += cpExchange.BtcVolume;
                    }
                    else
                    {
                        c.Exchanges = new List<Coin.Exchange> {cpExchange};
                        c.TotalVolume = cpExchange.BtcVolume;
                        c.HasImplementedMarketApi = true;
                    }

                    _po.CancellationToken.ThrowIfCancellationRequested();
                }
            }));
        }

        public void UpdateBittrex()
        {
            BittrexPairs bt = JsonControl.DownloadSerializedApi<BittrexPairs>(
                _client.GetStreamAsync("http://bittrex.com/api/v1.1/public/getmarketsummaries").Result);

            Parallel.ForEach(ListOfCoins, c => Parallel.ForEach(bt.Results, _po, btCoin =>
            {
                String[] splitMarket = btCoin.MarketName.Split('-');
                if (splitMarket[0] == "BTC" && splitMarket[1] == c.TagName)
                {
                    double priceToUse;
                    switch (_bidRecentAsk)
                    {
                        case 0:
                            priceToUse = btCoin.Bid;
                            break;
                        case 1:
                            priceToUse = btCoin.Last;
                            break;
                        case 2:
                            priceToUse = btCoin.Ask;
                            break;
                        default:
                            priceToUse = btCoin.Bid;
                            break;
                    }

                    Coin.Exchange btExchange = new Coin.Exchange
                    {
                        ExchangeName = "Bittrex",
                        BtcPrice = priceToUse,
                        BtcVolume = btCoin.BaseVolume,
                        BuyOrders = new List<Coin.Exchange.Order>(),
                        SellOrders = new List<Coin.Exchange.Order>()
                    };

                    if (int.Parse(btCoin.OpenBuyOrders) > 0 && int.Parse(btCoin.OpenSellOrders) > 0)
                    {
                        BittrexOrders btOrders = JsonControl.DownloadSerializedApi<BittrexOrders>(
                            _client.GetStreamAsync("https://bittrex.com/api/v1.1/public/getorderbook?market=" 
                            + btCoin.MarketName + "&type=both&depth=50 ").Result);
                        foreach (BittrexOrders.Results.Order result in btOrders.Result.Buy)
                        {
                            Coin.Exchange.Order order = new Coin.Exchange.Order
                            {
                                BtcPrice = result.Rate,
                                BtcVolume = result.Quantity * result.Rate,
                                CoinVolume = result.Quantity
                            };
                            btExchange.BuyOrders.Add(order);
                        }

                        foreach (BittrexOrders.Results.Order result in btOrders.Result.Sell)
                        {
                            Coin.Exchange.Order order = new Coin.Exchange.Order
                            {
                                BtcPrice = result.Rate,
                                BtcVolume = result.Quantity * result.Rate,
                                CoinVolume = result.Quantity
                            };
                            btExchange.SellOrders.Add(order);
                        }
                    }
                    
                    
                    if (c.HasImplementedMarketApi)
                    {
                        c.Exchanges.Add(btExchange);
                        c.TotalVolume += btExchange.BtcVolume;
                    }
                    else
                    {
                        c.Exchanges = new List<Coin.Exchange> {btExchange};
                        c.TotalVolume = btExchange.BtcVolume;
                        c.HasImplementedMarketApi = true;
                    }

                    _po.CancellationToken.ThrowIfCancellationRequested();
                }
            }));
        }

        public void UpdatePoloniex()
        {
            Dictionary<string, PoloniexPairs> pol = JsonControl.DownloadSerializedApi<Dictionary<string, PoloniexPairs>>(
                _client.GetStreamAsync("http://poloniex.com/public?command=returnTicker").Result);

            Parallel.ForEach(ListOfCoins, c => Parallel.ForEach(pol, _po, polCoin =>
            {
                String[] splitMarket = polCoin.Key.Split('_');
                if (splitMarket[0] == "BTC" && splitMarket[1] == c.TagName)
                {
                    double priceToUse;
                    switch (_bidRecentAsk)
                    {
                        case 0:
                            priceToUse = polCoin.Value.HighestBid;
                            break;
                        case 1:
                            priceToUse = polCoin.Value.Last;
                            break;
                        case 2:
                            priceToUse = polCoin.Value.LowestAsk;
                            break;
                        default:
                            priceToUse = polCoin.Value.HighestBid;
                            break;
                    }

                    Coin.Exchange polExchange = new Coin.Exchange
                    {
                        ExchangeName = "Poloniex",
                        BtcPrice = priceToUse,
                        BtcVolume = polCoin.Value.BaseVolume,
                        BuyOrders = new List<Coin.Exchange.Order>(),
                        SellOrders = new List<Coin.Exchange.Order>()
                    };

                    PoloniexOrders polOrders = JsonControl.DownloadSerializedApi<PoloniexOrders>(
                        _client.GetStreamAsync("https://poloniex.com/public?command=returnOrderBook&currencyPair=" 
                        + polCoin.Key + "&depth=50").Result);

                    if (polOrders.Bids != null && polOrders.Bids.Any())
                    {
                        foreach (double[] newOrder in polOrders.Bids)
                        {
                            Coin.Exchange.Order order = new Coin.Exchange.Order
                            {
                                BtcPrice = newOrder[0],
                                BtcVolume = newOrder[0]*newOrder[1],
                                CoinVolume = newOrder[1]
                            };
                            polExchange.BuyOrders.Add(order);
                        }
                    }

                    if (polOrders.Asks != null && polOrders.Asks.Any()) 
                    {
                        foreach (double[] newOrder in polOrders.Asks)
                        {
                            Coin.Exchange.Order order = new Coin.Exchange.Order
                            {
                                BtcPrice = newOrder[0],
                                BtcVolume = newOrder[0] * newOrder[1],
                                CoinVolume = newOrder[1]
                            };
                            polExchange.SellOrders.Add(order);
                        }
                    }

                    if (polCoin.Value.IsFrozen == "1")
                    {
                        polExchange.IsFrozen = true;
                    }

                    if (c.HasImplementedMarketApi)
                    {
                        c.Exchanges.Add(polExchange);
                        c.TotalVolume += polExchange.BtcVolume;
                    }
                    else
                    {
                        c.Exchanges = new List<Coin.Exchange> {polExchange};
                        c.TotalVolume = polExchange.BtcVolume;
                        c.HasImplementedMarketApi = true;
                    }

                    _po.CancellationToken.ThrowIfCancellationRequested();
                }
            }));
        }

        public void UpdateAllCoin()
        {
            AllCoinPairs ac = JsonControl.DownloadSerializedApi<AllCoinPairs>(
                _client.GetStreamAsync("https://www.allcoin.com/api2/pairs").Result);

            Parallel.ForEach(ListOfCoins, c => Parallel.ForEach(ac.Data, _po, acCoin =>
            {
                String[] splitMarket = acCoin.Key.Split('_');
                if (splitMarket[1] == "BTC" && splitMarket[0] == c.TagName)
                {
                    double volume, price;
                    bool hasOrder;

                    switch (_bidRecentAsk)
                    {
                        case 0:
                            hasOrder = Double.TryParse(acCoin.Value.TopBid, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out price);
                            break;
                        case 1:
                            hasOrder = Double.TryParse(acCoin.Value.TradePrice, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out price);
                            break;
                        case 2:
                            hasOrder = Double.TryParse(acCoin.Value.TopAsk, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out price);
                            break;
                        default:
                            hasOrder = Double.TryParse(acCoin.Value.TopBid, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out price);
                            break;
                    }

                    if (Double.TryParse(acCoin.Value.Volume24HBtc, NumberStyles.Float, 
                        CultureInfo.InvariantCulture, out volume) && hasOrder)
                    {
                        Coin.Exchange acExchange = new Coin.Exchange
                        {
                            ExchangeName = "AllCoin",
                            BtcVolume = volume,
                            BtcPrice = price,
                            BuyOrders = new List<Coin.Exchange.Order>(),
                            SellOrders = new List<Coin.Exchange.Order>()
                        };

                        AllCoinOrders acOrders = JsonControl.DownloadSerializedApi<AllCoinOrders>(
                            _client.GetStreamAsync("https://www.allcoin.com/api2/depth/" 
                            + acCoin.Key).Result);
                        foreach (KeyValuePair<string, double> newOrder in acOrders.Data.Buy)
                        {
                            double orderPrice;
                            if (double.TryParse(newOrder.Key, NumberStyles.Float, 
                                CultureInfo.InvariantCulture, out orderPrice))
                            {
                                Coin.Exchange.Order order = new Coin.Exchange.Order
                                {
                                    BtcPrice = orderPrice,
                                    BtcVolume = orderPrice*newOrder.Value,
                                    CoinVolume = newOrder.Value
                                };
                                acExchange.BuyOrders.Add(order);
                            }
                        }

                        foreach (KeyValuePair<string, double> newOrder in acOrders.Data.Sell)
                        {
                            double orderPrice;
                            if (double.TryParse(newOrder.Key, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out orderPrice))
                            {
                                Coin.Exchange.Order order = new Coin.Exchange.Order
                                {
                                    BtcPrice = orderPrice,
                                    BtcVolume = orderPrice * newOrder.Value,
                                    CoinVolume = newOrder.Value
                                };
                                acExchange.SellOrders.Add(order);
                            }
                        }

                        if (acCoin.Value.Status != "1" || acCoin.Value.WalletStatus != "1")
                        {
                            acExchange.IsFrozen = true;
                        }

                        if (c.HasImplementedMarketApi)
                        {
                            c.Exchanges.Add(acExchange);
                            c.TotalVolume += acExchange.BtcVolume;
                        }
                        else
                        {
                            c.Exchanges = new List<Coin.Exchange> {acExchange};
                            c.TotalVolume = acExchange.BtcVolume;
                            c.HasImplementedMarketApi = true;
                        }
                    }

                    _po.CancellationToken.ThrowIfCancellationRequested();
                }
            }));
        }

        public void UpdateAllCrypt()
        {
            Cryptsy ac = JsonControl.DownloadSerializedApi<Cryptsy>(
                _client.GetStreamAsync("https://www.allcrypt.com/api?method=marketdatav2").Result);

            Parallel.ForEach(ListOfCoins, c => Parallel.ForEach(ac.Returns.Markets, _po, acCoin =>
            {
                if (acCoin.Value.SecondaryCode == "BTC" && ((acCoin.Value.PrimaryCode == c.TagName)
                    || (acCoin.Value.PrimaryCode == "STR" && c.TagName == "STAR")))
                {
                    double priceToUse;
                    switch (_bidRecentAsk)
                    {
                        case 0:
                            priceToUse = acCoin.Value.BuyOrders != null
                                && acCoin.Value.BuyOrders.Any()
                                ? acCoin.Value.BuyOrders[0].Price
                                : acCoin.Value.LastTradePrice;
                            break;
                        case 1:
                            priceToUse = acCoin.Value.LastTradePrice;
                            break;
                        case 2:
                            priceToUse = acCoin.Value.SellOrders != null
                                && acCoin.Value.SellOrders.Any()
                                ? acCoin.Value.SellOrders[0].Price
                                : acCoin.Value.LastTradePrice;
                            break;
                        default:
                            priceToUse = acCoin.Value.BuyOrders != null
                                && acCoin.Value.BuyOrders.Any()
                                ? acCoin.Value.BuyOrders[0].Price
                                : acCoin.Value.LastTradePrice;
                            break;
                    }

                    Coin.Exchange acExchange = new Coin.Exchange
                    {
                        ExchangeName = "AllCrypt",
                        BtcPrice = priceToUse,
                        BtcVolume = (acCoin.Value.Volume * priceToUse),
                        BuyOrders = new List<Coin.Exchange.Order>(),
                        SellOrders = new List<Coin.Exchange.Order>()
                    };

                    if (acCoin.Value.BuyOrders != null && acCoin.Value.BuyOrders.Any())
                    {
                        foreach (Cryptsy.Return.Market.Order newOrder in acCoin.Value.BuyOrders)
                        {
                            Coin.Exchange.Order order = new Coin.Exchange.Order
                            {
                                BtcPrice = newOrder.Price,
                                BtcVolume = newOrder.Total,
                                CoinVolume = newOrder.Quantity
                            };
                            acExchange.BuyOrders.Add(order);
                        }
                    }

                    if (acCoin.Value.SellOrders != null && acCoin.Value.SellOrders.Any())
                    {
                        foreach (Cryptsy.Return.Market.Order newOrder in acCoin.Value.SellOrders)
                        {
                            Coin.Exchange.Order order = new Coin.Exchange.Order
                            {
                                BtcPrice = newOrder.Price,
                                BtcVolume = newOrder.Total,
                                CoinVolume = newOrder.Quantity
                            };
                            acExchange.SellOrders.Add(order);
                        }
                    }

                    if (c.HasImplementedMarketApi)
                    {
                        c.Exchanges.Add(acExchange);
                        c.TotalVolume += acExchange.BtcVolume;
                    }
                    else
                    {
                        c.Exchanges = new List<Coin.Exchange> { acExchange };
                        c.TotalVolume = acExchange.BtcVolume;
                        c.HasImplementedMarketApi = true;
                    }

                    _po.CancellationToken.ThrowIfCancellationRequested();
                }
            }));
        }

        public void UpdateCCex()
        {
            Dictionary<string, CCexPair> ccPairs = JsonControl.DownloadSerializedApi<Dictionary<string, CCexPair>>(
                _client.GetStreamAsync("https://c-cex.com/t/prices.json").Result);
            CCexVolume ccVolumes = JsonControl.DownloadSerializedApi<CCexVolume>(
                _client.GetStreamAsync("https://c-cex.com/t/s.html?a=lastvolumes&h=24").Result);

            Parallel.ForEach(ListOfCoins, c => Parallel.For(0, ccPairs.Count, _po, i =>
            /*foreach (Coin c in List)
            {
                for(int i = 0; i < ccPairs.Count; i++)*/
                {
                    var splitPair = ccPairs.Keys.ElementAt(i).Split('-');

                    if (splitPair[1] == "btc" && splitPair[0] == c.TagName.ToLowerInvariant())
                    {
                        CCexPair ccPair = ccPairs.Values.ElementAt(i);
                        double priceToUse;
                        switch (_bidRecentAsk)
                        {
                            case 0:
                                priceToUse = ccPair.Buy;
                                break;
                            case 1:
                                priceToUse = ccPair.Lastprice;
                                break;
                            case 2:
                                priceToUse = ccPair.Sell;
                                break;
                            default:
                                priceToUse = ccPair.Buy;
                                break;
                        }
                        
                        ParallelOptions optionsVolumeLoop = new ParallelOptions
                        {
                            CancellationToken = new CancellationTokenSource().Token,
                        };
                        double volumeToUse = 0;
                        Parallel.ForEach(ccVolumes.Returns, optionsVolumeLoop, ccVolume =>
                        //foreach (Dictionary<string, string> ccVolume in ccVolumes.Returns)
                        {
                            if (ccVolume.ContainsKey("volume_btc") && ccVolume.ContainsKey("volume_" + splitPair[0]) &&
                                Double.TryParse(ccVolume["volume_btc"], NumberStyles.Any, CultureInfo.InvariantCulture, out volumeToUse))
                            {
                                //break;
                                optionsVolumeLoop.CancellationToken.ThrowIfCancellationRequested();
                            }
                        });

                        Coin.Exchange ccExchange = new Coin.Exchange
                        {
                            ExchangeName = "C-Cex",
                            BtcVolume = volumeToUse,
                            BtcPrice = priceToUse
                        };

                        //Not able to get orders from C-Cex since you need a private API key for something so ridiculous

                        if (c.HasImplementedMarketApi)
                        {
                            c.Exchanges.Add(ccExchange);
                            c.TotalVolume += ccExchange.BtcVolume;
                        }
                        else
                        {
                            c.Exchanges = new List<Coin.Exchange> { ccExchange };
                            c.TotalVolume = ccExchange.BtcVolume;
                            c.HasImplementedMarketApi = true;
                        }

                        _po.CancellationToken.ThrowIfCancellationRequested();
                    }
                //}
            }));
        }

        public void UpdateComkort()
        {
            ComkortPairs com = JsonControl.DownloadSerializedApi<ComkortPairs>(
                _client.GetStreamAsync("https://api.comkort.com/v1/public/market/summary").Result);

            Parallel.ForEach(ListOfCoins, c => Parallel.ForEach(com.Markets, _po, comCoin =>
            {
                /*foreach (Coin c in List)
                {
                    foreach (KeyValuePair<string, Comkort.Pair> comCoin in com.Markets)
                    {*/
                        if (comCoin.Value.CurrencyCode == "BTC" && comCoin.Value.ItemCode == c.TagName)
                        {
                            double priceToUse;
                            switch (_bidRecentAsk)
                            {
                                case 0:
                                    priceToUse = comCoin.Value.BuyOrders != null
                                        && comCoin.Value.BuyOrders.Any()
                                        ? comCoin.Value.BuyOrders[0].Price
                                        : comCoin.Value.LastPrice;
                                    break;
                                case 1:
                                    priceToUse = comCoin.Value.LastPrice;
                                    break;
                                case 2:
                                    priceToUse = comCoin.Value.SellOrders != null
                                        && comCoin.Value.SellOrders.Any()
                                        ? comCoin.Value.SellOrders[0].Price
                                        : comCoin.Value.LastPrice;
                                    break;
                                default:
                                    priceToUse = comCoin.Value.BuyOrders != null 
                                        && comCoin.Value.BuyOrders.Any()
                                        ? comCoin.Value.BuyOrders[0].Price
                                        : comCoin.Value.LastPrice;
                                    break;
                            }

                            Coin.Exchange comExchange = new Coin.Exchange
                            {
                                ExchangeName = "Comkort",
                                BtcPrice = priceToUse,
                                BtcVolume = (comCoin.Value.CurrencyVolume),
                                BuyOrders = new List<Coin.Exchange.Order>(),
                                SellOrders = new List<Coin.Exchange.Order>()
                            };

                            ComkortOrders comOrders = JsonControl.DownloadSerializedApi<ComkortOrders>(
                                _client.GetStreamAsync("https://api.comkort.com/v1/public/order/list?market_alias=" 
                                + comCoin.Value.ItemCode + "_BTC").Result);
                            foreach (ComkortOrders.Orders.Order newOrder in comOrders.OrderData.Buy)
                            {
                                double price, volume, coinVolume;
                                if (Double.TryParse(newOrder.Price, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out price) &&
                                    Double.TryParse(newOrder.TotalPrice, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out volume) &&
                                    Double.TryParse(newOrder.Amount, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out coinVolume))
                                {
                                    Coin.Exchange.Order order = new Coin.Exchange.Order
                                    {
                                        BtcPrice = price,
                                        BtcVolume = volume,
                                        CoinVolume = coinVolume
                                    };
                                    comExchange.BuyOrders.Add(order);
                                }
                            }

                            foreach (ComkortOrders.Orders.Order newOrder in comOrders.OrderData.Sell)
                            {
                                double price, volume, coinVolume;
                                if (Double.TryParse(newOrder.Price, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out price) &&
                                    Double.TryParse(newOrder.TotalPrice, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out volume) &&
                                    Double.TryParse(newOrder.Amount, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out coinVolume))
                                {
                                    Coin.Exchange.Order order = new Coin.Exchange.Order
                                    {
                                        BtcPrice = price,
                                        BtcVolume = volume,
                                        CoinVolume = coinVolume
                                    };
                                    comExchange.SellOrders.Add(order);
                                }
                            }

                            if (c.HasImplementedMarketApi)
                            {
                                c.Exchanges.Add(comExchange);
                                c.TotalVolume += comExchange.BtcVolume;
                            }
                            else
                            {
                                c.Exchanges = new List<Coin.Exchange> {comExchange};
                                c.TotalVolume = comExchange.BtcVolume;
                                c.HasImplementedMarketApi = true;
                            }

                            _po.CancellationToken.ThrowIfCancellationRequested();
                        }
                    //}
                }));
        }

        public void UpdateCryptoine()
        {
            CryptoinePairs cry = JsonControl.DownloadSerializedApi<CryptoinePairs>(
                _client.GetStreamAsync("https://cryptoine.com/api/1/markets").Result);

            Parallel.ForEach(ListOfCoins, c => Parallel.ForEach(cry.Data, _po, cryCoin =>
            {
                string[] split = cryCoin.Key.Split('_');
                if (split[1] == "btc" && split[0].ToUpperInvariant() == c.TagName)
                {
                    double priceToUse;
                    switch (_bidRecentAsk)
                    {
                        case 0:
                            if (!double.TryParse(cryCoin.Value.Buy, NumberStyles.Float, 
                                CultureInfo.InvariantCulture, out priceToUse))
                            {
                                priceToUse = cryCoin.Value.Last;
                            }
                            break;
                        case 1:
                            priceToUse = cryCoin.Value.Last;
                            break;
                        case 2:
                            if (!double.TryParse(cryCoin.Value.Sell, NumberStyles.Float, 
                                CultureInfo.InvariantCulture, out priceToUse))
                            {
                                priceToUse = cryCoin.Value.Last;
                            }
                            break;
                        default:
                            if (!double.TryParse(cryCoin.Value.Buy, NumberStyles.Float, 
                                CultureInfo.InvariantCulture, out priceToUse))
                            {
                                priceToUse = cryCoin.Value.Last;
                            }
                            break;
                    }

                    Coin.Exchange cryExchange = new Coin.Exchange
                    {
                        ExchangeName = "Cryptoine",
                        BtcPrice = priceToUse,
                        BtcVolume = (cryCoin.Value.VolBase),
                        BuyOrders = new List<Coin.Exchange.Order>(),
                        SellOrders = new List<Coin.Exchange.Order>()
                    };

                    CryptoineOrders cryOrders = JsonControl.DownloadSerializedApi<CryptoineOrders>(
                        _client.GetStreamAsync("https://cryptoine.com/api/1/depth/" 
                        + cryCoin.Key).Result);
                    foreach (double[] newOrder in cryOrders.Bids)
                    {
                        Coin.Exchange.Order order = new Coin.Exchange.Order
                        {
                            BtcPrice = newOrder[0],
                            BtcVolume = newOrder[0]*newOrder[1],
                            CoinVolume = newOrder[1]
                        };
                        cryExchange.BuyOrders.Add(order);
                    }

                    foreach (double[] newOrder in cryOrders.Asks)
                    {
                        Coin.Exchange.Order order = new Coin.Exchange.Order
                        {
                            BtcPrice = newOrder[0],
                            BtcVolume = newOrder[0] * newOrder[1],
                            CoinVolume = newOrder[1]
                        };
                        cryExchange.SellOrders.Add(order);
                    }

                    if (c.HasImplementedMarketApi)
                    {
                        c.Exchanges.Add(cryExchange);
                        c.TotalVolume += cryExchange.BtcVolume;
                    }
                    else
                    {
                        c.Exchanges = new List<Coin.Exchange> { cryExchange };
                        c.TotalVolume = cryExchange.BtcVolume;
                        c.HasImplementedMarketApi = true;
                    }

                    _po.CancellationToken.ThrowIfCancellationRequested();
                }
                //}
            }));
        }

        public void UpdateBTer()
        {
            Dictionary<string, BTerPairs> btPairs = JsonControl.DownloadSerializedApi<Dictionary<string, BTerPairs>>(
                _client.GetStreamAsync("http://data.bter.com/api/1/tickers").Result);

            Parallel.ForEach(ListOfCoins, c => Parallel.ForEach(btPairs, _po, btCoin =>
            {
                /*foreach (Coin c in List)
                {
                    foreach (KeyValuePair<string, ...)
                    {*/
                string[] split = btCoin.Key.Split('_');
                if (split[1] == "btc" && split[0].ToUpperInvariant() == c.TagName)
                {
                    double priceToUse;
                    switch (_bidRecentAsk)
                    {
                        case 0:
                            if (!double.TryParse(btCoin.Value.Buy, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out priceToUse)
                                && !double.TryParse(btCoin.Value.Last, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out priceToUse))
                            {
                                priceToUse = 0;
                            }
                            break;
                        case 1:
                            if (!double.TryParse(btCoin.Value.Last, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out priceToUse))
                            {
                                priceToUse = 0;
                            }
                            break;
                        case 2:
                            if (!double.TryParse(btCoin.Value.Sell, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out priceToUse)
                                && !double.TryParse(btCoin.Value.Last, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out priceToUse))
                            {
                                priceToUse = 0;
                            }
                            break;
                        default:
                            if (!double.TryParse(btCoin.Value.Buy, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out priceToUse)
                                && !double.TryParse(btCoin.Value.Last, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out priceToUse))
                            {
                                priceToUse = 0;
                            }
                            break;
                    }

                    Coin.Exchange btExchange = new Coin.Exchange
                    {
                        ExchangeName = "BTer",
                        BtcPrice = priceToUse,
                        BtcVolume = Convert.ToDouble(btCoin.Value.Vols["vol_btc"].ToString()),
                        BuyOrders = new List<Coin.Exchange.Order>(),
                        SellOrders = new List<Coin.Exchange.Order>()
                    };

                    BTerOrders btOrders = JsonControl.DownloadSerializedApi<BTerOrders>(
                        _client.GetStreamAsync("http://data.bter.com/api/1/depth/"
                        + btCoin.Key).Result);
                    foreach (double[] newOrder in btOrders.Bids)
                    {
                        Coin.Exchange.Order order = new Coin.Exchange.Order
                        {
                            BtcPrice = newOrder[0],
                            BtcVolume = newOrder[0] * newOrder[1],
                            CoinVolume = newOrder[1]
                        };
                        btExchange.BuyOrders.Add(order);
                    }

                    foreach (double[] newOrder in btOrders.Asks)
                    {
                        Coin.Exchange.Order order = new Coin.Exchange.Order
                        {
                            BtcPrice = newOrder[0],
                            BtcVolume = newOrder[0] * newOrder[1],
                            CoinVolume = newOrder[1]
                        };
                        btExchange.SellOrders.Add(order);
                    }

                    if (c.HasImplementedMarketApi)
                    {
                        c.Exchanges.Add(btExchange);
                        c.TotalVolume += btExchange.BtcVolume;
                    }
                    else
                    {
                        c.Exchanges = new List<Coin.Exchange> { btExchange };
                        c.TotalVolume = btExchange.BtcVolume;
                        c.HasImplementedMarketApi = true;
                    }

                    _po.CancellationToken.ThrowIfCancellationRequested();
                }
                //}
            }));
        }

        public void UpdateAtomicTrade()
        {
            List<AtomicTradePair> atPairs = JsonControl.DownloadSerializedApi<List<AtomicTradePair>>(
                _client.GetStreamAsync("https://www.atomic-trade.com/SimpleAPI?a=marketsv2").Result);

            Parallel.ForEach(ListOfCoins, c => Parallel.ForEach(atPairs, _po, atCoin =>
            {
            /*foreach (Coin c in List)
            {
                foreach (var atCoin in atPairs)
                {*/
                    string[] split = atCoin.Market.Split('/');
                    if (split[1] == "BTC" && split[0] == c.TagName)
                    {
                        AtomicTradeOrders atOrders = JsonControl.DownloadSerializedApi<AtomicTradeOrders>(
                            _client.GetStreamAsync("https://www.atomic-trade.com/SimpleAPI?a=orderbook&p=BTC&c=" +
                                                   c.TagName).Result);

                        double priceToUse;
                        switch (_bidRecentAsk)
                        {
                            case 0:
                                if (atOrders.Market.Buyorders == null || !atOrders.Market.Buyorders.Any()
                                    || !double.TryParse(atOrders.Market.Buyorders[0].Price,
                                        NumberStyles.Float, CultureInfo.InvariantCulture, out priceToUse))
                                {
                                    priceToUse = 0;
                                }
                                break;
                            case 1:
                                if (!double.TryParse(atCoin.Price, NumberStyles.Float, 
                                    CultureInfo.InvariantCulture, out priceToUse))
                                {
                                    priceToUse = 0;
                                }
                                break;
                            case 2:
                                if (atOrders.Market.Sellorders == null || !atOrders.Market.Sellorders.Any()
                                    || !double.TryParse(atOrders.Market.Sellorders[0].Price,
                                        NumberStyles.Float, CultureInfo.InvariantCulture, out priceToUse))
                                {
                                    priceToUse = 0;
                                }
                                break;
                            default:
                                if (atOrders.Market.Buyorders == null || !atOrders.Market.Buyorders.Any()
                                    || !double.TryParse(atOrders.Market.Buyorders[0].Price,
                                        NumberStyles.Float, CultureInfo.InvariantCulture, out priceToUse))
                                {
                                    priceToUse = 0;
                                }
                                break;
                        }

                        Coin.Exchange atExchange = new Coin.Exchange
                        {
                            ExchangeName = "Atomic Trade",
                            BtcPrice = priceToUse,
                            BtcVolume = double.Parse(atCoin.Volume, NumberStyles.Float,
                            CultureInfo.InvariantCulture)*priceToUse,
                            BuyOrders = new List<Coin.Exchange.Order>(),
                            SellOrders = new List<Coin.Exchange.Order>()
                        };

                        if (atOrders.Market.Buyorders != null && atOrders.Market.Buyorders.Any())
                        {
                            foreach (AtomicTradeOrders.MarketData.Order newOrder in atOrders.Market.Buyorders)
                            {
                                double price, volume, coinVolume;
                                if (double.TryParse(newOrder.Price, NumberStyles.Float, 
                                    CultureInfo.InvariantCulture,out price)
                                    && double.TryParse(newOrder.Total, NumberStyles.Float, 
                                    CultureInfo.InvariantCulture,out volume)
                                    && double.TryParse(newOrder.Quantity, NumberStyles.Float, 
                                    CultureInfo.InvariantCulture, out coinVolume))
                                {
                                    Coin.Exchange.Order order = new Coin.Exchange.Order
                                    {
                                        BtcPrice = price,
                                        BtcVolume = volume,
                                        CoinVolume = coinVolume
                                    };
                                    atExchange.BuyOrders.Add(order);
                                }
                            }
                        }

                        if (atOrders.Market.Sellorders != null && atOrders.Market.Sellorders.Any())
                        {
                            foreach (AtomicTradeOrders.MarketData.Order newOrder in atOrders.Market.Sellorders)
                            {
                                double price, volume, coinVolume;
                                if (double.TryParse(newOrder.Price, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out price)
                                    && double.TryParse(newOrder.Total, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out volume)
                                    && double.TryParse(newOrder.Quantity, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out coinVolume))
                                {
                                    Coin.Exchange.Order order = new Coin.Exchange.Order
                                    {
                                        BtcPrice = price,
                                        BtcVolume = volume,
                                        CoinVolume = coinVolume
                                    };
                                    atExchange.SellOrders.Add(order);
                                }
                            }
                        }

                        if (atCoin.Error == "1")
                        {
                            atExchange.IsFrozen = true;
                        }

                        if (c.HasImplementedMarketApi)
                        {
                            c.Exchanges.Add(atExchange);
                            c.TotalVolume += atExchange.BtcVolume;
                        }
                        else
                        {
                            c.Exchanges = new List<Coin.Exchange> {atExchange};
                            c.TotalVolume = atExchange.BtcVolume;
                            c.HasImplementedMarketApi = true;
                        }

                        _po.CancellationToken.ThrowIfCancellationRequested();
                    }
                //}}
            }));
        }

        public void UpdatePoolPicker(decimal average, bool reviewCalc)
        {
            DateTime whenToEnd = DateTime.UtcNow - new TimeSpan((int) average, 0, 0,0);

            PoolPicker pp = JsonControl.DownloadSerializedApi<PoolPicker>(
                _client.GetStreamAsync("http://poolpicker.eu/fullapi").Result);
            foreach (PoolPicker.Pool pool in pp.Pools)
            {
                double reviewPercentage, rating;
                if (Double.TryParse(pool.Rating, NumberStyles.Float, CultureInfo.InvariantCulture, out rating))
                {
                    reviewPercentage = rating/5;
                }
                else
                {
                    reviewPercentage = 1;
                }

                foreach (KeyValuePair<string, List<PoolPicker.Pool.Algo>> algoResults in pool.PoolProfitability)
                {
                    AddPoolPickerPool(pool,algoResults.Value, algoResults.Key.ToUpperInvariant(),  whenToEnd, reviewCalc, reviewPercentage);
                }
            }
        }

        private void AddPoolPickerPool(PoolPicker.Pool pool, List<PoolPicker.Pool.Algo> profitList, string algo, 
            DateTime whenToEnd, bool reviewCalc, double reviewPercentage)
        {
            Coin c = new Coin
            {
                HasImplementedMarketApi = true,
                IsMultiPool = true,
            };
            Coin.Exchange ppExchange = new Coin.Exchange { ExchangeName = pool.Name, };
            c.Exchanges.Add(ppExchange);

            c.Algo = algo;
            c.FullName = pool.Name + " " + c.Algo + " (PP)";
            c.TagName = "PP" + pool.Id + c.Algo;

            double dAverage = 0;
            int iCounter;
            for (iCounter = 0; iCounter < profitList.Count; iCounter++)
            {
                PoolPicker.Pool.Algo profit = profitList[iCounter];
                DateTime profitDate = DateTime.ParseExact(profit.Date, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

                if (profitDate.Date < whenToEnd.Date)
                {
                    break;
                }

                dAverage += profit.Btc;
                
                if (profitDate.Date.Equals(whenToEnd.Date)) break;
            }

            c.Exchanges[0].BtcPrice = 
                c.Algo == "KECCAK" || c.Algo == "SHA256"
                ? dAverage/(iCounter + 1)
                : dAverage/(iCounter + 1)*1000;

            if (reviewCalc)
            {
                c.Exchanges[0].BtcPrice *= reviewPercentage;
            }

            c.Source = "PoolPicker.eu";
            c.Retrieved = DateTime.Now;

            Add(c);
        }

        public void UpdateCrypToday(decimal average)
        {
            CrypToday ct = JsonControl.DownloadSerializedApi<CrypToday>(
                _client.GetStreamAsync("http://cryp.today/data").Result);
            Coin[] tempMultipools = new Coin[ct.Cols.Count-1];

            Parallel.For(0, ct.Cols.Count - 1, _po, i =>
            {
                string[] splitNameAndAlgo = ct.Cols[i + 1].Label.Split(' ');
                tempMultipools[i] = new Coin();
                switch (splitNameAndAlgo[1])
                {
                    case "X11":
                    case "X13":
                        tempMultipools[i].Algo = splitNameAndAlgo[1];
                        break;
                    case "N":
                        tempMultipools[i].Algo = "SCRYPTN";
                        break;
                    //case "S":
                    default:
                        tempMultipools[i].Algo = "SCRYPT";
                        break;
                }

                tempMultipools[i].FullName = ct.Cols[i + 1].Label + " (CT)";
                tempMultipools[i].TagName = "CT" + i + tempMultipools[i].Algo;

                tempMultipools[i].HasImplementedMarketApi = true;
                tempMultipools[i].IsMultiPool = true;

                Coin.Exchange ctExchange = new Coin.Exchange {ExchangeName = splitNameAndAlgo[0]};
                tempMultipools[i].Exchanges.Add(ctExchange);
            });

            for (int i = ct.Rows.Count - 1; i >= ct.Rows.Count - average; i--)
            {
                int row = i;
                Parallel.For(1, ct.Rows[i].Results.Count, _po, column =>
                {
                    double priceHolder;
                    if (!string.IsNullOrWhiteSpace(ct.Rows[row].Results[column].Btc) &&
                        double.TryParse(ct.Rows[row].Results[column].Btc, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out priceHolder))
                    {
                        tempMultipools[column - 1].Exchanges[0].BtcPrice += priceHolder;
                        // Temp storing amount of not-null BtcPerDays into BlockReward
                        tempMultipools[column - 1].BlockReward++;
                    }
                });
            }

            foreach (Coin c in tempMultipools)
            {
                switch (c.Algo)
                {
                    case "X11":
                        c.Exchanges[0].BtcPrice /= 5.2;
                        break;
                    case "X13":
                        c.Exchanges[0].BtcPrice /= 3;
                        break;
                    case "SCRYPTN":
                        c.Exchanges[0].BtcPrice /= 0.47;
                        break;
                }

                c.Exchanges[0].BtcPrice /= c.BlockReward;
                c.Exchanges[0].BtcPrice *= 1000;
                c.BlockReward = 0;

                c.Source = "Cryp.Today";
                c.Retrieved = DateTime.Now;

                Add(c);
            }
        }

        public void CalculatePrices(bool useWeightedCalculation, bool calcFiat, bool use24HDiff)
        {
            double usdPrice = 0, eurPrice = 0, gbpPrice = 0, cnyPrice = 0;

            if (calcFiat)
            {
                CoinDesk cd = JsonControl.DownloadSerializedApi<CoinDesk>(
                    _client.GetStreamAsync("https://api.coindesk.com/v1/bpi/currentprice.json").Result);
                usdPrice = cd.BpiPrice.UsdPrice.RateFloat;
                eurPrice = cd.BpiPrice.EurPrice.RateFloat;
                gbpPrice = cd.BpiPrice.GbpPrice.RateFloat;

                cd = JsonControl.DownloadSerializedApi<CoinDesk>(
                        _client.GetStreamAsync("https://api.coindesk.com/v1/bpi/currentprice/CNY.json").Result);
                cnyPrice = cd.BpiPrice.CnyPrice.RateFloat;
            }

            Parallel.ForEach(ListOfCoins, coin => Parallel.ForEach(UsedProfile.CustomAlgoList, _po, algo =>
            {
                if (coin.Algo == algo.Name)
                {
                    coin.CalcProfitability(algo.HashRate, useWeightedCalculation, UsedProfile.Multiplier, algo.Style, algo.Target, use24HDiff);

                    if (calcFiat)
                    {
                        double fiatElectricityCost = (algo.Wattage / 1000) * 24 * UsedProfile.FiatPerKwh;
                        switch (UsedProfile.FiatOfChoice)
                        {
                            case 1:
                                coin.BtcPerDay -= (fiatElectricityCost/eurPrice);
                                break;
                            case 2:
                                coin.BtcPerDay -= (fiatElectricityCost/gbpPrice);
                                break;
                            case 3:
                                coin.BtcPerDay -= (fiatElectricityCost/cnyPrice);
                                break;
                            default:
                                coin.BtcPerDay -= (fiatElectricityCost/usdPrice);
                                break;
                        }


                        coin.UsdPerDay = coin.BtcPerDay*usdPrice;
                        coin.EurPerDay = coin.BtcPerDay*eurPrice;
                        coin.GbpPerDay = coin.BtcPerDay*gbpPrice;
                        coin.CnyPerDay = coin.BtcPerDay*cnyPrice;
                    }

                    _po.CancellationToken.ThrowIfCancellationRequested();
                }
            }));

            ListOfCoins = ListOfCoins.AsParallel().OrderByDescending(o => o.BtcPerDay).ToList();
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Coin c in ListOfCoins)
            {
                sb.Append(c + Environment.NewLine);
            }

            return sb.ToString();
        }
    }
}