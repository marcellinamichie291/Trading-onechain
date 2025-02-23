﻿/*
MIT LICENSE

Copyright 2017 Digital Ruby, LLC - http://www.digitalruby.com

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

namespace ExchangeSharp
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    public sealed class ExchangeBitfinexAPI : ExchangeAPI
    {
        public override string BaseUrl { get; set; } = "https://api.bitfinex.com/v2";
        public override string BaseUrlWebSocket { get; set; } = "wss://api.bitfinex.com/ws";
        public override string Name => ExchangeName.Bitfinex;

        public Dictionary<string, string> DepositMethodLookup { get; }

        public string BaseUrlV1 { get; set; } = "https://api.bitfinex.com/v1";

        public ExchangeBitfinexAPI()
        {
            NonceStyle = NonceStyle.UnixMillisecondsString;
            RateLimit = new RateGate(1, TimeSpan.FromSeconds(6.0));

            // List is from "Withdrawal Types" section https://docs.bitfinex.com/v1/reference#rest-auth-withdrawal
            DepositMethodLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AID"] = "aid",
                ["AVT"] = "aventus",
                ["BAT"] = "bat",
                ["BCH"] = "bcash",
                ["BTC"] = "bitcoin",
                ["BTG"] = "bgold",
                ["DASH"] = "dash", // TODO: Bitfinex returns "DSH" as the symbol name in the API but on the site it is "DASH". How to normalize?
                ["EDO"] = "eidoo",
                ["ELF"] = "elf",
                ["EOS"] = "eos",
                ["ETC"] = "ethereumc",
                ["ETH"] = "ethereum",
                ["FUN"] = "fun",
                ["GNT"] = "golem",
                ["LTC"] = "litecoin",
                ["MIOTA"] = "iota",
                ["MNA"] = "mna",
                ["NEO"] = "neo",
                ["OMG"] = "omisego",
                ["QASH"] = "qash",
                ["QTUM"] = "qtum",
                ["RCN"] = "rcn",
                ["REP"] = "rep",
                ["RLC"] = "rlc",
                ["SAN"] = "santiment",
                ["SNG"] = "sng",
                ["SNT"] = "status",
                ["SPK"] = "spk",
                ["TNB"] = "tnb",
                ["TRX"] = "trx",
                //["?USDTO?"] = "tetheruso", // Tether on OMNI - Don't use until it's clear how this works
                //["?USDTE?"] = "tetheruse", // Tether on Ethereum - Don't use until it's clear how this works
                ["XMR"] = "monero",
                ["XRP"] = "ripple",
                ["YYW"] = "yoyow",
                ["ZEC"] = "zcash",
                ["ZRX"] = "zrx",
            };
        }

        public override string NormalizeSymbol(string symbol)
        {
            return (symbol ?? string.Empty).Replace("-", string.Empty).ToUpperInvariant();
        }

        public string NormalizeSymbolV1(string symbol)
        {
            return (symbol ?? string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        }

        public async Task<IEnumerable<ExchangeOrderResult>> GetOrderDetailsInternalV2(string url, string symbol = null)
        {
            Dictionary<string, object> payload = GetNoncePayload();
            payload["limit"] = 250;
            payload["start"] = DateTime.UtcNow.Subtract(TimeSpan.FromDays(365.0)).UnixTimestampFromDateTimeMilliseconds();
            payload["end"] = DateTime.UtcNow.UnixTimestampFromDateTimeMilliseconds();
            JToken result = await MakeJsonRequestAsync<JToken>(url, null, payload);
            CheckError(result);
            Dictionary<string, List<JToken>> trades = new Dictionary<string, List<JToken>>(StringComparer.OrdinalIgnoreCase);
            if (result is JArray array)
            {
                foreach (JToken token in array)
                {
                    if (symbol == null || token[1].ToStringInvariant() == "t" + symbol.ToUpperInvariant())
                    {
                        string lookup = token[1].ToStringInvariant().Substring(1).ToLowerInvariant();
                        if (!trades.TryGetValue(lookup, out List<JToken> tradeList))
                        {
                            tradeList = trades[lookup] = new List<JToken>();
                        }
                        tradeList.Add(token);
                    }
                }
            }
            return ParseOrderV2(trades);
        }

        protected override async Task<IEnumerable<string>> OnGetSymbolsAsync()
        {
            var m = await GetSymbolsMetadataAsync();
            return m.Select(x => x.MarketName);
        }

        protected override async Task<IEnumerable<ExchangeMarket>> OnGetSymbolsMetadataAsync()
        {
            if (ReadCache("GetSymbols", out List<ExchangeMarket> cachedMarkets))
            {
                return cachedMarkets;
            }

            var markets = new List<ExchangeMarket>();

            JToken allPairs = await MakeJsonRequestAsync<JToken>("/symbols_details", BaseUrlV1);
            Match m;

            foreach (JToken pair in allPairs)
            {
                var market = new ExchangeMarket
                {
                    IsActive = true,
                    MarketName = NormalizeSymbol(pair["pair"].ToStringInvariant()),
                    MinTradeSize = pair["minimum_order_size"].ConvertInvariant<decimal>()
                };
                m = Regex.Match(market.MarketName, "^(BTC|USD|ETH|GBP|JPY|EUR)");
                if (m.Success)
                {
                    market.MarketCurrency = m.Value;
                    market.BaseCurrency = market.MarketName.Substring(m.Length);
                }
                else
                {
                    m = Regex.Match(market.MarketName, "(BTC|USD|ETH|GBP|JPY|EUR)$");
                    if (m.Success)
                    {
                        market.MarketCurrency = market.MarketName.Substring(0, m.Index);
                        market.BaseCurrency = m.Value;
                    }
                    else
                    {
                        throw new System.IO.InvalidDataException("Unexpected market name: " + market.MarketName);
                    }
                }
                int pricePrecision = pair["price_precision"].ConvertInvariant<int>();
                market.PriceStepSize = (decimal)Math.Pow(0.1, pricePrecision);
                markets.Add(market);
            }

            WriteCache("GetSymbols", TimeSpan.FromMinutes(60.0), markets);
            return markets;
        }

        protected override async Task<ExchangeTicker> OnGetTickerAsync(string symbol)
        {
            symbol = NormalizeSymbol(symbol);
            decimal[] ticker = await MakeJsonRequestAsync<decimal[]>("/ticker/t" + symbol);
            return new ExchangeTicker { Bid = ticker[0], Ask = ticker[2], Last = ticker[6], Volume = new ExchangeVolume { BaseVolume = ticker[7], BaseSymbol = symbol, ConvertedVolume = ticker[7] * ticker[6], ConvertedSymbol = symbol, Timestamp = DateTime.UtcNow } };
        }

        protected override async Task<IEnumerable<KeyValuePair<string, ExchangeTicker>>> OnGetTickersAsync()
        {
            List<KeyValuePair<string, ExchangeTicker>> tickers = new List<KeyValuePair<string, ExchangeTicker>>();
            IReadOnlyCollection<string> symbols = (await GetSymbolsAsync()).ToArray();
            if (symbols != null && symbols.Count != 0)
            {
                StringBuilder symbolString = new StringBuilder();
                foreach (string symbol in symbols)
                {
                    symbolString.Append('t');
                    symbolString.Append(symbol.ToUpperInvariant());
                    symbolString.Append(',');
                }
                symbolString.Length--;
                JToken token = await MakeJsonRequestAsync<JToken>("/tickers?symbols=" + symbolString);
                DateTime now = DateTime.UtcNow;
                foreach (JArray array in token)
                {
                    tickers.Add(new KeyValuePair<string, ExchangeTicker>(array[0].ToStringInvariant().Substring(1), new ExchangeTicker
                    {
                        Ask = array[3].ConvertInvariant<decimal>(),
                        Bid = array[1].ConvertInvariant<decimal>(),
                        Last = array[7].ConvertInvariant<decimal>(),
                        Volume = new ExchangeVolume
                        {
                            BaseVolume = array[8].ConvertInvariant<decimal>(),
                            BaseSymbol = array[0].ToStringInvariant(),
                            ConvertedVolume = array[8].ConvertInvariant<decimal>() * array[7].ConvertInvariant<decimal>(),
                            ConvertedSymbol = array[0].ToStringInvariant(),
                            Timestamp = now
                        }
                    }));
                }
            }
            return tickers;
        }

        public override IDisposable GetTickersWebSocket(System.Action<IReadOnlyCollection<KeyValuePair<string, ExchangeTicker>>> callback)
        {
            if (callback == null)
            {
                return null;
            }
            Dictionary<int, string> channelIdToSymbol = new Dictionary<int, string>();
            return ConnectWebSocket(string.Empty, (msg, _socket) =>
            {
                try
                {
                    JToken token = JToken.Parse(msg);
                    if (token is JArray array)
                    {
                        if (array.Count > 10)
                        {
                            List<KeyValuePair<string, ExchangeTicker>> tickerList = new List<KeyValuePair<string, ExchangeTicker>>();
                            if (channelIdToSymbol.TryGetValue(array[0].ConvertInvariant<int>(), out string symbol))
                            {
                                ExchangeTicker ticker = ParseTickerWebSocket(symbol, array);
                                if (ticker != null)
                                {
                                    callback(new KeyValuePair<string, ExchangeTicker>[] { new KeyValuePair<string, ExchangeTicker>(symbol, ticker) });
                                }
                            }
                        }
                    }
                    else if (token["event"].ToStringInvariant() == "subscribed" && token["channel"].ToStringInvariant() == "ticker")
                    {
                        // {"event":"subscribed","channel":"ticker","chanId":1,"pair":"BTCUSD"}
                        int channelId = token["chanId"].ConvertInvariant<int>();
                        channelIdToSymbol[channelId] = token["pair"].ToStringInvariant();
                    }
                }
                catch
                {
                }
            }, (_socket) =>
            {
                var symbols = GetSymbols();
                foreach (var symbol in symbols)
                {
                    _socket.SendMessage("{\"event\":\"subscribe\",\"channel\":\"ticker\",\"pair\":\"" + symbol + "\"}");
                }
            });
        }

        protected override async Task<ExchangeOrderBook> OnGetOrderBookAsync(string symbol, int maxCount = 100)
        {
            symbol = NormalizeSymbol(symbol);
            ExchangeOrderBook orders = new ExchangeOrderBook();
            decimal[][] books = await MakeJsonRequestAsync<decimal[][]>("/book/t" + symbol + "/P0?len=" + maxCount);
            foreach (decimal[] book in books)
            {
                if (book[2] > 0m)
                {
                    orders.Bids.Add(new ExchangeOrderPrice { Amount = book[2], Price = book[0] });
                }
                else
                {
                    orders.Asks.Add(new ExchangeOrderPrice { Amount = -book[2], Price = book[0] });
                }
            }
            return orders;
        }

        protected override async Task OnGetHistoricalTradesAsync(System.Func<IEnumerable<ExchangeTrade>, bool> callback, string symbol, DateTime? sinceDateTime = null)
        {
            const int maxCount = 100;
            symbol = NormalizeSymbol(symbol);
            string baseUrl = "/trades/t" + symbol + "/hist?sort=" + (sinceDateTime == null ? "-1" : "1") + "&limit=" + maxCount;
            string url;
            List<ExchangeTrade> trades = new List<ExchangeTrade>();
            decimal[][] tradeChunk;
            while (true)
            {
                url = baseUrl;
                if (sinceDateTime != null)
                {
                    url += "&start=" + (long)CryptoUtility.UnixTimestampFromDateTimeMilliseconds(sinceDateTime.Value);
                }
                tradeChunk = await MakeJsonRequestAsync<decimal[][]>(url);
                if (tradeChunk == null || tradeChunk.Length == 0)
                {
                    break;
                }
                if (sinceDateTime != null)
                {
                    sinceDateTime = CryptoUtility.UnixTimeStampToDateTimeMilliseconds((double)tradeChunk[tradeChunk.Length - 1][1]);
                }
                foreach (decimal[] tradeChunkPiece in tradeChunk)
                {
                    trades.Add(new ExchangeTrade { Amount = Math.Abs(tradeChunkPiece[2]), IsBuy = tradeChunkPiece[2] > 0m, Price = tradeChunkPiece[3], Timestamp = CryptoUtility.UnixTimeStampToDateTimeMilliseconds((double)tradeChunkPiece[1]), Id = (long)tradeChunkPiece[0] });
                }
                trades.Sort((t1, t2) => t1.Timestamp.CompareTo(t2.Timestamp));
                if (!callback(trades))
                {
                    break;
                }
                trades.Clear();
                if (tradeChunk.Length < 500 || sinceDateTime == null)
                {
                    break;
                }
                await Task.Delay(5000);
            }
        }

        protected override async Task<IEnumerable<MarketCandle>> OnGetCandlesAsync(string symbol, int periodSeconds, DateTime? startDate = null, DateTime? endDate = null, int? limit = null)
        {
            // https://api.bitfinex.com/v2/candles/trade:1d:btcusd/hist?start=ms_start&end=ms_end
            List<MarketCandle> candles = new List<MarketCandle>();
            symbol = NormalizeSymbol(symbol);
            string periodString = CryptoUtility.SecondsToPeriodString(periodSeconds).Replace("d", "D"); // WTF Bitfinex, capital D???
            string url = "/candles/trade:" + periodString + ":t" + symbol + "/hist?sort=1";
            if (startDate != null || endDate != null)
            {
                endDate = endDate ?? DateTime.UtcNow;
                startDate = startDate ?? endDate.Value.Subtract(TimeSpan.FromDays(1.0));
                url += "&start=" + ((long)startDate.Value.UnixTimestampFromDateTimeMilliseconds()).ToStringInvariant();
                url += "&end=" + ((long)endDate.Value.UnixTimestampFromDateTimeMilliseconds()).ToStringInvariant();
            }
            if (limit != null)
            {
                url += "&limit=" + (limit.Value.ToStringInvariant());
            }
            JToken token = await MakeJsonRequestAsync<JToken>(url);
            CheckError(token);

            /* MTS, OPEN, CLOSE, HIGH, LOW, VOL */
            foreach (JArray candle in token)
            {
                candles.Add(new MarketCandle
                {
                    ClosePrice = candle[2].ConvertInvariant<decimal>(),
                    ExchangeName = Name,
                    HighPrice = candle[3].ConvertInvariant<decimal>(),
                    LowPrice = candle[4].ConvertInvariant<decimal>(),
                    Name = symbol,
                    OpenPrice = candle[1].ConvertInvariant<decimal>(),
                    PeriodSeconds = periodSeconds,
                    Timestamp = CryptoUtility.UnixTimeStampToDateTimeMilliseconds(candle[0].ConvertInvariant<long>()),
                    BaseVolume = candle[5].ConvertInvariant<double>(),
                    ConvertedVolume = candle[5].ConvertInvariant<double>() * candle[2].ConvertInvariant<double>()
                });
            }

            return candles;
        }

        protected override async Task<Dictionary<string, decimal>> OnGetAmountsAsync()
        {
            Dictionary<string, decimal> lookup = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            JArray obj = await MakeJsonRequestAsync<Newtonsoft.Json.Linq.JArray>("/balances", BaseUrlV1, GetNoncePayload());
            CheckError(obj);
            foreach (JToken token in obj)
            {
                if (token["type"].ToStringInvariant() == "exchange")
                {
                    decimal amount = token["amount"].ConvertInvariant<decimal>();
                    if (amount > 0m)
                    {
                        lookup[token["currency"].ToStringInvariant()] = amount;
                    }
                }
            }
            return lookup;
        }

        protected override async Task<Dictionary<string, decimal>> OnGetAmountsAvailableToTradeAsync()
        {
            Dictionary<string, decimal> lookup = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            JArray obj = await MakeJsonRequestAsync<Newtonsoft.Json.Linq.JArray>("/balances", BaseUrlV1, GetNoncePayload());
            CheckError(obj);
            foreach (JToken token in obj)
            {
                if (token["type"].ToStringInvariant() == "exchange")
                {
                    decimal amount = token["available"].ConvertInvariant<decimal>();
                    if (amount > 0m)
                    {
                        lookup[token["currency"].ToStringInvariant()] = amount;
                    }
                }
            }
            return lookup;
        }

        protected override async Task<ExchangeOrderResult> OnPlaceOrderAsync(ExchangeOrderRequest order)
        {
            string symbol = NormalizeSymbolV1(order.Symbol);
            Dictionary<string, object> payload = GetNoncePayload();
            payload["symbol"] = symbol;
            payload["amount"] = order.RoundAmount().ToStringInvariant();
            payload["side"] = (order.IsBuy ? "buy" : "sell");
            payload["type"] = (order.OrderType == OrderType.Market ? "exchange market" : "exchange limit");
            if (order.OrderType != OrderType.Market)
            {
                payload["price"] = order.Price.ToStringInvariant();
            }
            foreach (var kv in order.ExtraParameters)
            {
                payload[kv.Key] = kv.Value;
            }

            JToken obj = await MakeJsonRequestAsync<JToken>("/order/new", BaseUrlV1, payload);
            CheckError(obj);
            return ParseOrder(obj);
        }

        protected override async Task<ExchangeOrderResult> OnGetOrderDetailsAsync(string orderId, string symbol = null)
        {
            if (string.IsNullOrWhiteSpace(orderId))
            {
                return null;
            }

            Dictionary<string, object> payload = GetNoncePayload();
            payload["order_id"] = long.Parse(orderId);
            JToken result = await MakeJsonRequestAsync<JToken>("/order/status", BaseUrlV1, payload);
            CheckError(result);
            return ParseOrder(result);
        }

        protected override async Task<IEnumerable<ExchangeOrderResult>> OnGetOpenOrderDetailsAsync(string symbol = null)
        {
            return await GetOrderDetailsInternalAsync("/orders", symbol);
        }

        protected override async Task<IEnumerable<ExchangeOrderResult>> OnGetCompletedOrderDetailsAsync(string symbol = null, DateTime? afterDate = null)
        {
            string cacheKey = "GetCompletedOrderDetails_" + (symbol ?? string.Empty) + "_" + (afterDate == null ? string.Empty : afterDate.Value.Ticks.ToStringInvariant());
            if (!ReadCache<ExchangeOrderResult[]>(cacheKey, out ExchangeOrderResult[] orders))
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    // HACK: Bitfinex does not provide a way to get all historical order details beyond a few days in one call, so we have to
                    //  get the historical details one by one for each symbol.
                    var symbols = (await GetSymbolsAsync()).Where(s => s.IndexOf("usd", StringComparison.OrdinalIgnoreCase) < 0 && s.IndexOf("btc", StringComparison.OrdinalIgnoreCase) >= 0);
                    orders = (await GetOrderDetailsInternalV1(symbols, afterDate)).ToArray();
                }
                else
                {
                    symbol = NormalizeSymbol(symbol);
                    orders = (await GetOrderDetailsInternalV1(new string[] { symbol }, afterDate)).ToArray();
                }

                // Bitfinex gets angry if this is called more than once a minute
                WriteCache(cacheKey, TimeSpan.FromMinutes(2.0), orders);
            }
            return orders;
        }

        public override IDisposable GetCompletedOrderDetailsWebSocket(System.Action<ExchangeOrderResult> callback)
        {
            if (callback == null)
            {
                return null;
            }

            return ConnectWebSocket(string.Empty, (msg, _socket) =>
            {
                try
                {
                    JToken token = JToken.Parse(msg);
                    if (token is JArray array && array.Count > 1 && array[2] is JArray && array[1].ToStringInvariant() == "os")
                    {
                        foreach (JToken orderToken in array[2])
                        {
                            callback.Invoke(ParseOrderWebSocket(orderToken));
                        }
                    }
                }
                catch
                {
                }
            }, (_socket) =>
            {
                object nonce = GenerateNonce();
                string authPayload = "AUTH" + nonce;
                string signature = CryptoUtility.SHA384Sign(authPayload, PrivateApiKey.ToUnsecureString());
                Dictionary<string, object> payload = new Dictionary<string, object>
                {
                    { "apiKey", PublicApiKey.ToUnsecureString() },
                    { "event", "auth" },
                    { "authPayload", authPayload },
                    { "authSig", signature }
                };
                string payloadJSON = GetJsonForPayload(payload);
                _socket.SendMessage(payloadJSON);
            });
        }

        protected override async Task OnCancelOrderAsync(string orderId, string symbol = null)
        {
            Dictionary<string, object> payload = GetNoncePayload();
            payload["order_id"] = long.Parse(orderId);
            JObject result = await MakeJsonRequestAsync<JObject>("/order/cancel", BaseUrlV1, payload);
            CheckError(result);
        }

        protected override async Task<ExchangeDepositDetails> OnGetDepositAddressAsync(string symbol, bool forceRegenerate = false)
        {
            // IOTA addresses should never be used more than once
            if (symbol.Equals("MIOTA", StringComparison.OrdinalIgnoreCase))
            {
                forceRegenerate = true;
            }

            // symbol needs to be translated to full name of coin: bitcoin/litecoin/ethereum
            if (!DepositMethodLookup.TryGetValue(symbol, out string fullName))
            {
                return null;
            }

            Dictionary<string, object> payload = GetNoncePayload();
            payload["method"] = fullName;
            payload["wallet_name"] = "exchange";
            payload["renew"] = forceRegenerate ? 1 : 0;

            JToken result = await MakeJsonRequestAsync<JToken>("/deposit/new", BaseUrlV1, payload, "POST");
            CheckError(result);

            var details = new ExchangeDepositDetails
            {
                Symbol = result["currency"].ToStringInvariant(),
            };

            if (result["address_pool"] != null)
            {
                details.Address = result["address_pool"].ToStringInvariant();
                details.AddressTag = result["address"].ToStringLowerInvariant();
            }
            else
            {
                details.Address = result["address"].ToStringInvariant();
            }

            return details;
        }

        /// <summary>Gets the deposit history for a symbol</summary>
        /// <param name="symbol">The symbol to check. Must be specified.</param>
        /// <returns>Collection of ExchangeCoinTransfers</returns>
        protected override async Task<IEnumerable<ExchangeTransaction>> OnGetDepositHistoryAsync(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentNullException(nameof(symbol));
            }

            Dictionary<string, object> payload = GetNoncePayload();
            payload["currency"] = NormalizeSymbol(symbol);

            JToken result = await MakeJsonRequestAsync<JToken>("/history/movements", BaseUrlV1, payload, "POST");
            CheckError(result);
            var transactions = new List<ExchangeTransaction>();
            foreach (JToken token in result)
            {
                if (!string.Equals(token["type"].ToStringUpperInvariant(), "DEPOSIT"))
                {
                    continue;
                }

                var transaction = new ExchangeTransaction
                {
                    PaymentId = token["id"].ToStringInvariant(),
                    BlockchainTxId = token["txid"].ToStringInvariant(),
                    Symbol = token["currency"].ToStringUpperInvariant(),
                    Notes = token["description"].ToStringInvariant() + ", method: " + token["method"].ToStringInvariant(),
                    Amount = token["amount"].ConvertInvariant<decimal>(),
                    Address = token["address"].ToStringInvariant()
                };

                string status = token["status"].ToStringUpperInvariant();
                switch (status)
                {
                    case "COMPLETED":
                        transaction.Status = TransactionStatus.Complete;
                        break;
                    case "UNCONFIRMED":
                        transaction.Status = TransactionStatus.Processing;
                        break;
                    default:
                        transaction.Status = TransactionStatus.Unknown;
                        transaction.Notes += ", Unknown transaction status " + status;
                        break;
                }

                double unixTimestamp = token["timestamp"].ConvertInvariant<double>();
                transaction.TimestampUTC = unixTimestamp.UnixTimeStampToDateTimeSeconds();
                transaction.TxFee = token["fee"].ConvertInvariant<decimal>();

                transactions.Add(transaction);
            }

            return transactions;
        }

        /// <summary>A withdrawal request.</summary>
        /// <param name="withdrawalRequest">The withdrawal request.
        /// NOTE: Network fee must be subtracted from amount or withdrawal will fail</param>
        /// <returns>The withdrawal response</returns>
        protected override async Task<ExchangeWithdrawalResponse> OnWithdrawAsync(ExchangeWithdrawalRequest withdrawalRequest)
        {
            string symbol = NormalizeSymbol(withdrawalRequest.Symbol);

            // symbol needs to be translated to full name of coin: bitcoin/litecoin/ethereum
            if (!DepositMethodLookup.TryGetValue(symbol, out string fullName))
            {
                return null;
            }

            // Bitfinex adds the fee on top of what you request to withdrawal
            if (withdrawalRequest.TakeFeeFromAmount)
            {
                Dictionary<string, decimal> fees = await GetWithdrawalFeesAsync();
                if (fees.TryGetValue(symbol, out decimal feeAmt))
                {
                    withdrawalRequest.Amount -= feeAmt;
                }
            }

            Dictionary<string, object> payload = GetNoncePayload();
            payload["withdraw_type"] = fullName;
            payload["walletselected"] = "exchange";
            payload["amount"] = withdrawalRequest.Amount.ToString(CultureInfo.InvariantCulture); // API throws if this is a number not a string
            payload["address"] = withdrawalRequest.Address;

            if (!string.IsNullOrWhiteSpace(withdrawalRequest.AddressTag))
            {
                payload["payment_id"] = withdrawalRequest.AddressTag;
            }

            if (!string.IsNullOrWhiteSpace(withdrawalRequest.Description))
            {
                payload["account_name"] = withdrawalRequest.Description;
            }

            JToken result = await MakeJsonRequestAsync<JToken>("/withdraw", BaseUrlV1, payload, "POST");

            var resp = new ExchangeWithdrawalResponse();
            if (!string.Equals(result[0]["status"].ToStringInvariant(), "success", StringComparison.OrdinalIgnoreCase))
            {
                resp.Success = false;
            }

            resp.Id = result[0]["withdrawal_id"].ToStringInvariant();
            resp.Message = result[0]["message"].ToStringInvariant();
            return resp;
        }

        protected override void ProcessRequest(HttpWebRequest request, Dictionary<string, object> payload)
        {
            if (CanMakeAuthenticatedRequest(payload))
            {
                request.Method = "POST";
                request.ContentType = request.Accept = "application/json";

                if (request.RequestUri.AbsolutePath.StartsWith("/v2"))
                {
                    string nonce = payload["nonce"].ToStringInvariant();
                    payload.Remove("nonce");
                    string json = JsonConvert.SerializeObject(payload);
                    string toSign = "/api" + request.RequestUri.PathAndQuery + nonce + json;
                    string hexSha384 = CryptoUtility.SHA384Sign(toSign, PrivateApiKey.ToUnsecureString());
                    request.Headers["bfx-nonce"] = nonce;
                    request.Headers["bfx-apikey"] = PublicApiKey.ToUnsecureString();
                    request.Headers["bfx-signature"] = hexSha384;
                    WriteFormToRequest(request, json);
                }
                else
                {
                    // bitfinex v1 doesn't put the payload in the post body it puts it in as a http header, so no need to write to request stream
                    payload.Add("request", request.RequestUri.AbsolutePath);
                    string json = JsonConvert.SerializeObject(payload);
                    string json64 = System.Convert.ToBase64String(Encoding.ASCII.GetBytes(json));
                    string hexSha384 = CryptoUtility.SHA384Sign(json64, PrivateApiKey.ToUnsecureString());
                    request.Headers["X-BFX-PAYLOAD"] = json64;
                    request.Headers["X-BFX-SIGNATURE"] = hexSha384;
                    request.Headers["X-BFX-APIKEY"] = PublicApiKey.ToUnsecureString();
                }
            }
        }

        protected override Task<IReadOnlyDictionary<string, ExchangeCurrency>> OnGetCurrenciesAsync()
        {
            throw new NotSupportedException("Bitfinex does not provide data about its currencies via the API");
        }

        private async Task<IEnumerable<ExchangeOrderResult>> GetOrderDetailsInternalAsync(string url, string symbol = null)
        {
            List<ExchangeOrderResult> orders = new List<ExchangeOrderResult>();
            symbol = NormalizeSymbolV1(symbol);
            JToken result = await MakeJsonRequestAsync<JToken>(url, BaseUrlV1, GetNoncePayload());
            CheckError(result);
            if (result is JArray array)
            {
                foreach (JToken token in array)
                {
                    if (symbol == null || token["symbol"].ToStringInvariant() == symbol)
                    {
                       orders.Add(ParseOrder(token));
                    }
                }
            }
            return orders;
        }

        private async Task<IEnumerable<ExchangeOrderResult>> GetOrderDetailsInternalV1(IEnumerable<string> symbols, DateTime? afterDate)
        {
            Dictionary<string, ExchangeOrderResult> orders = new Dictionary<string, ExchangeOrderResult>(StringComparer.OrdinalIgnoreCase);
            foreach (string symbol in symbols)
            {
                string normalizedSymbol = NormalizeSymbol(symbol);
                Dictionary<string, object> payload = GetNoncePayload();
                payload["symbol"] = normalizedSymbol;
                payload["limit_trades"] = 250;
                if (afterDate != null)
                {
                    payload["timestamp"] = afterDate.Value.UnixTimestampFromDateTimeSeconds().ToStringInvariant();
                    payload["until"] = DateTime.UtcNow.UnixTimestampFromDateTimeSeconds().ToStringInvariant();
                }
                JToken token = await MakeJsonRequestAsync<JToken>("/mytrades", BaseUrlV1, payload);
                CheckError(token);
                foreach (JToken trade in token)
                {
                    ExchangeOrderResult subOrder = ParseTrade(trade, normalizedSymbol);
                    lock (orders)
                    {
                        if (orders.TryGetValue(subOrder.OrderId, out ExchangeOrderResult baseOrder))
                        {
                            baseOrder.AppendOrderWithOrder(subOrder);
                        }
                        else
                        {
                            orders[subOrder.OrderId] = subOrder;
                        }
                    }
                }
            }
            return orders.Values.OrderByDescending(o => o.OrderDate);
        }

        private void CheckError(JToken result)
        {
            if (result != null && !(result is JArray) && result["result"] != null && result["result"].ToStringInvariant() == "error")
            {
                throw new APIException(result["reason"].ToStringInvariant());
            }
        }

        private ExchangeOrderResult ParseOrder(JToken order)
        {
            decimal amount = order["original_amount"].ConvertInvariant<decimal>();
            decimal amountFilled = order["executed_amount"].ConvertInvariant<decimal>();
            decimal price = order["price"].ConvertInvariant<decimal>();
            return new ExchangeOrderResult
            {
                Amount = amount,
                AmountFilled = amountFilled,
                Price = price,
                AveragePrice = order["avg_execution_price"].ConvertInvariant<decimal>(order["price"].ConvertInvariant<decimal>()),
                Message = string.Empty,
                OrderId = order["id"].ToStringInvariant(),
                Result = (amountFilled == amount ? ExchangeAPIOrderResult.Filled : (amountFilled == 0 ? ExchangeAPIOrderResult.Pending : ExchangeAPIOrderResult.FilledPartially)),
                OrderDate = CryptoUtility.UnixTimeStampToDateTimeSeconds(order["timestamp"].ConvertInvariant<double>()),
                Symbol = order["symbol"].ToStringInvariant(),
                IsBuy = order["side"].ToStringInvariant() == "buy"
            };
        }

        private ExchangeOrderResult ParseOrderWebSocket(JToken order)
        {
            /*
            [ 0, "os", [ [
                "<ORD_ID>",
                "<ORD_PAIR>",
                "<ORD_AMOUNT>",
                "<ORD_AMOUNT_ORIG>",
                "<ORD_TYPE>",
                "<ORD_STATUS>",
                "<ORD_PRICE>",
                "<ORD_PRICE_AVG>",
                "<ORD_CREATED_AT>",
                "<ORD_NOTIFY>",
                 "<ORD_HIDDEN>",
                "<ORD_OCO>"
            ] ] ];
            */

            decimal amount = order[2].ConvertInvariant<decimal>();
            return new ExchangeOrderResult
            {
                Amount = amount,
                AmountFilled = amount,
                Price = order[6].ConvertInvariant<decimal>(),
                AveragePrice = order[7].ConvertInvariant<decimal>(),
                IsBuy = (amount > 0m),
                OrderDate = CryptoUtility.UnixTimeStampToDateTimeMilliseconds(order[8].ConvertInvariant<long>()),
                OrderId = order[0].ToStringInvariant(),
                Result = ExchangeAPIOrderResult.Filled,
                Symbol = order[1].ToStringInvariant()
            };
        }

        private IEnumerable<ExchangeOrderResult> ParseOrderV2(Dictionary<string, List<JToken>> trades)
        {

            /*
            [
            ID	integer	Trade database id
            PAIR	string	Pair (BTCUSD, …)
            MTS_CREATE	integer	Execution timestamp
            ORDER_ID	integer	Order id
            EXEC_AMOUNT	float	Positive means buy, negative means sell
            EXEC_PRICE	float	Execution price
            ORDER_TYPE	string	Order type
            ORDER_PRICE	float	Order price
            MAKER	int	1 if true, 0 if false
            FEE	float	Fee
            FEE_CURRENCY	string	Fee currency
            ],
            */

            foreach (var kv in trades)
            {
                ExchangeOrderResult order = new ExchangeOrderResult { Result = ExchangeAPIOrderResult.Filled };
                foreach (JToken trade in kv.Value)
                {
                    ExchangeOrderResult append = new ExchangeOrderResult { Symbol = kv.Key, OrderId = trade[3].ToStringInvariant() };
                    append.Amount = append.AmountFilled = Math.Abs(trade[4].ConvertInvariant<decimal>());
                    append.Price = trade[7].ConvertInvariant<decimal>();
                    append.AveragePrice = trade[5].ConvertInvariant<decimal>();
                    append.IsBuy = trade[4].ConvertInvariant<decimal>() >= 0m;
                    append.OrderDate = CryptoUtility.UnixTimeStampToDateTimeMilliseconds(trade[2].ConvertInvariant<long>());
                    append.OrderId = trade[3].ToStringInvariant();
                    order.AppendOrderWithOrder(append);
                }
                yield return order;
            }
        }

        private ExchangeOrderResult ParseTrade(JToken trade, string symbol)
        {
            /*
            [{
              "price":"246.94",
              "amount":"1.0",
              "timestamp":"1444141857.0",
              "exchange":"",
              "type":"Buy",
              "fee_currency":"USD",
              "fee_amount":"-0.49388",
              "tid":11970839,
              "order_id":446913929
            }]
            */
            return new ExchangeOrderResult
            {
                Amount = trade["amount"].ConvertInvariant<decimal>(),
                AmountFilled = trade["amount"].ConvertInvariant<decimal>(),
                AveragePrice = trade["price"].ConvertInvariant<decimal>(),
                IsBuy = trade["type"].ToStringUpperInvariant() == "BUY",
                OrderDate = CryptoUtility.UnixTimeStampToDateTimeSeconds(trade["timestamp"].ConvertInvariant<double>()),
                OrderId = trade["order_id"].ToStringInvariant(),
                Result = ExchangeAPIOrderResult.Filled,
                Symbol = symbol
            };
        }

        private ExchangeTicker ParseTickerWebSocket(string symbol, JToken token)
        {
            decimal last = token[7].ConvertInvariant<decimal>();
            decimal volume = token[8].ConvertInvariant<decimal>();
            return new ExchangeTicker
            {
                Ask = token[3].ConvertInvariant<decimal>(),
                Bid = token[1].ConvertInvariant<decimal>(),
                Last = last,
                Volume = new ExchangeVolume
                {
                    BaseVolume = volume,
                    BaseSymbol = symbol,
                    ConvertedVolume = volume * last,
                    ConvertedSymbol = symbol,
                    Timestamp = DateTime.UtcNow
                }
            };
        }

        /// <summary>Gets the withdrawal fees for various currencies.</summary>
        /// <returns>A dictionary of symbol-fee pairs</returns>
        private async Task<Dictionary<string, decimal>> GetWithdrawalFeesAsync()
        {
            JToken obj = await MakeJsonRequestAsync<JToken>("/account_fees", BaseUrlV1, GetNoncePayload());
            CheckError(obj);

            var fees = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var jToken in obj["withdraw"])
            {
                var prop = (JProperty)jToken;
                fees[prop.Name] = prop.Value.ConvertInvariant<decimal>();
            }

            return fees;
        }
    }
}
