﻿/*
MIT LICENSE

Copyright 2017 Digital Ruby, LLC - http://www.digitalruby.com

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ExchangeSharp
{
    public sealed class ExchangeBinanceAPI : ExchangeAPI
    {
        public override string BaseUrl { get; set; } = "https://api.binance.com/api/v1";
        public override string BaseUrlWebSocket { get; set; } = "wss://stream.binance.com:9443";
        public string BaseUrlPrivate { get; set; } = "https://api.binance.com/api/v3";
        public string WithdrawalUrlPrivate { get; set; } = "https://api.binance.com/wapi/v3";
        public override string Name => ExchangeName.Binance;

        static ExchangeBinanceAPI()
        {
            ExchangeGlobalCurrencyReplacements[typeof(ExchangeBinanceAPI)] = new KeyValuePair<string, string>[]
            {
                new KeyValuePair<string, string>("BCC", "BCH")
            };
        }

        public ExchangeBinanceAPI()
        {
            // give binance plenty of room to accept requests
            RequestWindow = TimeSpan.FromMinutes(15.0);
            NonceStyle = NonceStyle.UnixMilliseconds;
            NonceOffset = TimeSpan.FromSeconds(10.0);
            SymbolSeparator = string.Empty;
        }

        public override string NormalizeSymbol(string symbol)
        {
            return (symbol ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).Replace("/", string.Empty).ToUpperInvariant();
        }

        public override string ExchangeSymbolToGlobalSymbol(string symbol)
        {
            // All pairs in Binance begin or end with BTC, ETH, or BNB
            if (symbol.Length == 6)
            {
                return ExchangeSymbolToGlobalSymbolWithSeparator((symbol.Substring(0, 3) + GlobalSymbolSeparator + symbol.Substring(3)), GlobalSymbolSeparator);
            }
            else if (symbol.StartsWith("BTC") || symbol.StartsWith("ETH") || symbol.StartsWith("BNB"))
            {
                return ExchangeSymbolToGlobalSymbolWithSeparator((symbol.Substring(3) + GlobalSymbolSeparator + symbol.Substring(0, 3)), GlobalSymbolSeparator);
            }

            // reversed
            return ExchangeSymbolToGlobalSymbolWithSeparator((symbol.Substring(symbol.Length - 3, 3) + GlobalSymbolSeparator + symbol.Substring(0, symbol.Length - 3)), GlobalSymbolSeparator);
        }

        protected override async Task<IEnumerable<string>> OnGetSymbolsAsync()
        {
            if (ReadCache("GetSymbols", out List<string> symbols))
            {
                return symbols;
            }

            symbols = new List<string>();
            JToken obj = await MakeJsonRequestAsync<JToken>("/ticker/allPrices");
            CheckError(obj);
            foreach (JToken token in obj)
            {
                // bug I think in the API returns numbers as symbol names... WTF.
                string symbol = token["symbol"].ToStringInvariant();
                if (!long.TryParse(symbol, out long tmp))
                {
                    symbols.Add(symbol);
                }
            }
            WriteCache("GetSymbols", TimeSpan.FromMinutes(60.0), symbols);
            return symbols;
        }

        protected override async Task<IEnumerable<ExchangeMarket>> OnGetSymbolsMetadataAsync()
        {
            /*
             *         {
            "symbol": "QTUMETH",
            "status": "TRADING",
            "baseAsset": "QTUM",
            "baseAssetPrecision": 8,
            "quoteAsset": "ETH",
            "quotePrecision": 8,
            "orderTypes": [
                "LIMIT",
                "LIMIT_MAKER",
                "MARKET",
                "STOP_LOSS_LIMIT",
                "TAKE_PROFIT_LIMIT"
            ],
            "icebergAllowed": true,
            "filters": [
                {
                    "filterType": "PRICE_FILTER",
                    "minPrice": "0.00000100",
                    "maxPrice": "100000.00000000",
                    "tickSize": "0.00000100"
                },
                {
                    "filterType": "LOT_SIZE",
                    "minQty": "0.01000000",
                    "maxQty": "90000000.00000000",
                    "stepSize": "0.01000000"
                },
                {
                    "filterType": "MIN_NOTIONAL",
                    "minNotional": "0.01000000"
                }
            ]
        },
             */

            var markets = new List<ExchangeMarket>();
            JToken obj = await MakeJsonRequestAsync<JToken>("/exchangeInfo");
            CheckError(obj);
            JToken allSymbols = obj["symbols"];
            foreach (JToken symbol in allSymbols)
            {
                var market = new ExchangeMarket
                {
                    MarketName = symbol["symbol"].ToStringUpperInvariant(),
                    IsActive = ParseMarketStatus(symbol["status"].ToStringUpperInvariant()),
                    BaseCurrency = symbol["quoteAsset"].ToStringUpperInvariant(),
                    MarketCurrency = symbol["baseAsset"].ToStringUpperInvariant()
                };

                // "LOT_SIZE"
                JToken filters = symbol["filters"];
                JToken lotSizeFilter = filters?.FirstOrDefault(x => string.Equals(x["filterType"].ToStringUpperInvariant(), "LOT_SIZE"));
                if (lotSizeFilter != null)
                {
                    market.MaxTradeSize = lotSizeFilter["maxQty"].ConvertInvariant<decimal>();
                    market.MinTradeSize = lotSizeFilter["minQty"].ConvertInvariant<decimal>();
                    market.QuantityStepSize = lotSizeFilter["stepSize"].ConvertInvariant<decimal>();
                }

                // PRICE_FILTER
                JToken priceFilter = filters?.FirstOrDefault(x => string.Equals(x["filterType"].ToStringUpperInvariant(), "PRICE_FILTER"));
                if (priceFilter != null)
                {
                    market.MaxPrice = priceFilter["maxPrice"].ConvertInvariant<decimal>();
                    market.MinPrice = priceFilter["minPrice"].ConvertInvariant<decimal>();
                    market.PriceStepSize = priceFilter["tickSize"].ConvertInvariant<decimal>();
                }
                markets.Add(market);
            }

            return markets;
        }

        protected override Task<IReadOnlyDictionary<string, ExchangeCurrency>> OnGetCurrenciesAsync()
        {
            throw new NotSupportedException("Binance does not provide data about its currencies via the API");
        }

        protected override async Task<ExchangeTicker> OnGetTickerAsync(string symbol)
        {
            symbol = NormalizeSymbol(symbol);
            JToken obj = await MakeJsonRequestAsync<JToken>("/ticker/24hr?symbol=" + symbol);
            CheckError(obj);
            return ParseTicker(symbol, obj);
        }

        protected override async Task<IEnumerable<KeyValuePair<string, ExchangeTicker>>> OnGetTickersAsync()
        {
            List<KeyValuePair<string, ExchangeTicker>> tickers = new List<KeyValuePair<string, ExchangeTicker>>();
            string symbol;
            JToken obj = await MakeJsonRequestAsync<JToken>("/ticker/24hr");
            CheckError(obj);
            foreach (JToken child in obj)
            {
                symbol = child["symbol"].ToStringInvariant();
                tickers.Add(new KeyValuePair<string, ExchangeTicker>(symbol, ParseTicker(symbol, child)));
            }
            return tickers;
        }

        public override IDisposable GetTickersWebSocket(System.Action<IReadOnlyCollection<KeyValuePair<string, ExchangeTicker>>> callback)
        {
            if (callback == null)
            {
                return null;
            }
            return ConnectWebSocket("/stream?streams=!ticker@arr", (msg, _socket) =>
            {
                try
                {
                    JToken token = JToken.Parse(msg);
                    List<KeyValuePair<string, ExchangeTicker>> tickerList = new List<KeyValuePair<string, ExchangeTicker>>();
                    ExchangeTicker ticker;
                    foreach (JToken childToken in token["data"])
                    {
                        ticker = ParseTickerWebSocket(childToken);
                        tickerList.Add(new KeyValuePair<string, ExchangeTicker>(ticker.Volume.BaseSymbol, ticker));
                    }
                    if (tickerList.Count != 0)
                    {
                        callback(tickerList);
                    }
                }
                catch
                {
                }
            });
        }

        protected override async Task<ExchangeOrderBook> OnGetOrderBookAsync(string symbol, int maxCount = 100)
        {
            symbol = NormalizeSymbol(symbol);
            JToken obj = await MakeJsonRequestAsync<JToken>("/depth?symbol=" + symbol + "&limit=" + maxCount);
            CheckError(obj);
            return ParseOrderBook(obj);
        }

        protected override async Task OnGetHistoricalTradesAsync(System.Func<IEnumerable<ExchangeTrade>, bool> callback, string symbol, DateTime? sinceDateTime = null)
        {
            /* [ {
            "a": 26129,         // Aggregate tradeId
		    "p": "0.01633102",  // Price
		    "q": "4.70443515",  // Quantity
		    "f": 27781,         // First tradeId
		    "l": 27781,         // Last tradeId
		    "T": 1498793709153, // Timestamp
		    "m": true,          // Was the buyer the maker?
		    "M": true           // Was the trade the best price match?
            } ] */

            symbol = NormalizeSymbol(symbol);
            string baseUrl = "/aggTrades?symbol=" + symbol;
            string url;
            List<ExchangeTrade> trades = new List<ExchangeTrade>();
            DateTime cutoff;
            if (sinceDateTime == null)
            {
                cutoff = DateTime.UtcNow;
            }
            else
            {
                cutoff = sinceDateTime.Value;
                sinceDateTime = DateTime.UtcNow;
            }
            url = baseUrl;

            while (true)
            {
                JArray obj = await MakeJsonRequestAsync<Newtonsoft.Json.Linq.JArray>(url);
                if (obj == null || obj.Count == 0)
                {
                    break;
                }
                if (sinceDateTime != null)
                {
                    sinceDateTime = CryptoUtility.UnixTimeStampToDateTimeMilliseconds(obj.First["T"].ConvertInvariant<long>());
                    if (sinceDateTime.Value < cutoff)
                    {
                        sinceDateTime = null;
                    }
                }
                if (sinceDateTime != null)
                {
                    url = baseUrl + "&startTime=" + ((long)CryptoUtility.UnixTimestampFromDateTimeMilliseconds(sinceDateTime.Value - TimeSpan.FromHours(1.0))).ToStringInvariant() +
                        "&endTime=" + ((long)CryptoUtility.UnixTimestampFromDateTimeMilliseconds(sinceDateTime.Value)).ToStringInvariant();
                }
                foreach (JToken token in obj)
                {
                    // TODO: Binance doesn't provide a buy or sell type, I've put in a request for them to add this
                    trades.Add(new ExchangeTrade
                    {
                        Amount = token["q"].ConvertInvariant<decimal>(),
                        Price = token["p"].ConvertInvariant<decimal>(),
                        Timestamp = CryptoUtility.UnixTimeStampToDateTimeMilliseconds(token["T"].ConvertInvariant<long>()),
                        Id = token["a"].ConvertInvariant<long>(),
                        IsBuy = token["m"].ConvertInvariant<bool>()
                    });
                }
                trades.Sort((t1, t2) => t1.Timestamp.CompareTo(t2.Timestamp));
                if (!callback(trades))
                {
                    break;
                }
                trades.Clear();
                if (sinceDateTime == null)
                {
                    break;
                }
                await Task.Delay(1000);
            }
        }

        protected override async Task<IEnumerable<MarketCandle>> OnGetCandlesAsync(string symbol, int periodSeconds, DateTime? startDate = null, DateTime? endDate = null, int? limit = null)
        {
            /* [
            [
		    1499040000000,      // Open time
		    "0.01634790",       // Open
		    "0.80000000",       // High
		    "0.01575800",       // Low
		    "0.01577100",       // Close
		    "148976.11427815",  // Volume
		    1499644799999,      // Close time
		    "2434.19055334",    // Quote asset volume
		    308,                // Number of trades
		    "1756.87402397",    // Taker buy base asset volume
		    "28.46694368",      // Taker buy quote asset volume
		    "17928899.62484339" // Can be ignored
		    ]] */

            List<MarketCandle> candles = new List<MarketCandle>();
            symbol = NormalizeSymbol(symbol);
            string url = "/klines?symbol=" + symbol;
            if (startDate != null)
            {
                url += "&startTime=" + (long)startDate.Value.UnixTimestampFromDateTimeMilliseconds();
                url += "&endTime=" + ((endDate == null ? long.MaxValue : (long)endDate.Value.UnixTimestampFromDateTimeMilliseconds())).ToStringInvariant();
            }
            if (limit != null)
            {
                url += "&limit=" + (limit.Value.ToStringInvariant());
            }
            string periodString = CryptoUtility.SecondsToPeriodString(periodSeconds);
            url += "&interval=" + periodString;
            JToken obj = await MakeJsonRequestAsync<JToken>(url);
            CheckError(obj);
            foreach (JArray array in obj)
            {
                candles.Add(new MarketCandle
                {
                    ClosePrice = array[4].ConvertInvariant<decimal>(),
                    ExchangeName = Name,
                    HighPrice = array[2].ConvertInvariant<decimal>(),
                    LowPrice = array[3].ConvertInvariant<decimal>(),
                    Name = symbol,
                    OpenPrice = array[1].ConvertInvariant<decimal>(),
                    PeriodSeconds = periodSeconds,
                    Timestamp = CryptoUtility.UnixTimeStampToDateTimeMilliseconds(array[0].ConvertInvariant<long>()),
                    BaseVolume = array[5].ConvertInvariant<double>(),
                    ConvertedVolume = array[7].ConvertInvariant<double>(),
                    WeightedAverage = 0m
                });
            }

            return candles;
        }

        protected override async Task<Dictionary<string, decimal>> OnGetAmountsAsync()
        {
            JToken token = await MakeJsonRequestAsync<JToken>("/account", BaseUrlPrivate, GetNoncePayload());
            CheckError(token);
            Dictionary<string, decimal> balances = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken balance in token["balances"])
            {
                decimal amount = balance["free"].ConvertInvariant<decimal>() + balance["locked"].ConvertInvariant<decimal>();
                if (amount > 0m)
                {
                    balances[balance["asset"].ToStringInvariant()] = amount;
                }
            }
            return balances;
        }

        protected override async Task<Dictionary<string, decimal>> OnGetAmountsAvailableToTradeAsync()
        {
            JToken token = await MakeJsonRequestAsync<JToken>("/account", BaseUrlPrivate, GetNoncePayload());
            CheckError(token);
            Dictionary<string, decimal> balances = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken balance in token["balances"])
            {
                decimal amount = balance["free"].ConvertInvariant<decimal>();
                if (amount > 0m)
                {
                    balances[balance["asset"].ToStringInvariant()] = amount;
                }
            }
            return balances;
        }

        protected override async Task<ExchangeOrderResult> OnPlaceOrderAsync(ExchangeOrderRequest order)
        {
            string symbol = NormalizeSymbol(order.Symbol);
            Dictionary<string, object> payload = GetNoncePayload();
            payload["symbol"] = symbol;
            payload["side"] = order.IsBuy ? "BUY" : "SELL";
            payload["type"] = order.OrderType.ToStringUpperInvariant();

            // Binance has strict rules on which prices and quantities are allowed. They have to match the rules defined in the market definition.
            decimal outputQuantity = ClampOrderQuantity(symbol, order.Amount);
            decimal outputPrice = ClampOrderPrice(symbol, order.Price);

            payload["quantity"] = outputQuantity;
            payload["newOrderRespType"] = "FULL";

            if (order.OrderType != OrderType.Market)
            {
                payload["timeInForce"] = "GTC";
                payload["price"] = outputPrice;
            }
            foreach (var kv in order.ExtraParameters)
            {
                payload[kv.Key] = kv.Value;
            }

            JToken token = await MakeJsonRequestAsync<JToken>("/order", BaseUrlPrivate, payload, "POST");
            CheckError(token);
            return ParseOrder(token);
        }

        protected override async Task<ExchangeOrderResult> OnGetOrderDetailsAsync(string orderId, string symbol = null)
        {
            Dictionary<string, object> payload = GetNoncePayload();
            if (string.IsNullOrEmpty(symbol))
            {
                throw new InvalidOperationException("Binance single order details request requires symbol");
            }
            symbol = NormalizeSymbol(symbol);
            payload["symbol"] = symbol;
            payload["orderId"] = orderId;
            JToken token = await MakeJsonRequestAsync<JToken>("/order", BaseUrlPrivate, payload);
            CheckError(token);
            ExchangeOrderResult result = ParseOrder(token);

            // Add up the fees from each trade in the order
            Dictionary<string, object> feesPayload = GetNoncePayload();
            feesPayload["symbol"] = symbol;
            JToken feesToken = await MakeJsonRequestAsync<JToken>("/myTrades", BaseUrlPrivate, feesPayload);
            CheckError(feesToken);
            ParseFees(feesToken, result);

            return result;
        }

        /// <summary>Process the trades that executed as part of your order and sum the fees.</summary>
        /// <param name="feesToken">The trades executed for a specific currency pair.</param>
        /// <param name="result">The result object to append to.</param>
        private static void ParseFees(JToken feesToken, ExchangeOrderResult result)
        {
            var tradesInOrder = feesToken.Where(x => x["orderId"].ToStringInvariant() == result.OrderId);

            bool currencySet = false;
            foreach (var trade in tradesInOrder)
            {
                result.Fees += trade["commission"].ConvertInvariant<decimal>();

                // TODO: Not sure how to handle commissions in different currencies, for example if you run out of BNB mid-trade
                if (!currencySet)
                {
                    result.FeesCurrency = trade["commissionAsset"].ToStringInvariant();
                    currencySet = true;
                }
            }
        }

        protected override async Task<IEnumerable<ExchangeOrderResult>> OnGetOpenOrderDetailsAsync(string symbol = null)
        {
            List<ExchangeOrderResult> orders = new List<ExchangeOrderResult>();
            Dictionary<string, object> payload = GetNoncePayload();
            if (!string.IsNullOrWhiteSpace(symbol))
            {
                payload["symbol"] = NormalizeSymbol(symbol);
            }
            JToken token = await MakeJsonRequestAsync<JToken>("/openOrders", BaseUrlPrivate, payload);
            CheckError(token);
            foreach (JToken order in token)
            {
                orders.Add(ParseOrder(order));
            }

            return orders;
        }

        private async Task<IEnumerable<ExchangeOrderResult>> GetCompletedOrdersForAllSymbolsAsync(DateTime? afterDate)
        {
            // TODO: This is a HACK, Binance API needs to add a single API call to get all orders for all symbols, terrible...
            List<ExchangeOrderResult> orders = new List<ExchangeOrderResult>();
            Exception ex = null;
            string failedSymbol = null;
            Parallel.ForEach((await GetSymbolsAsync()).Where(s => s.IndexOf("BTC", StringComparison.OrdinalIgnoreCase) >= 0), async (s) =>
            {
                try
                {
                    foreach (ExchangeOrderResult order in (await GetCompletedOrderDetailsAsync(s, afterDate)))
                    {
                        lock (orders)
                        {
                            orders.Add(order);
                        }
                    }
                }
                catch (Exception _ex)
                {
                    failedSymbol = s;
                    ex = _ex;
                }
            });

            if (ex != null)
            {
                throw new APIException("Failed to get completed order details for symbol " + failedSymbol, ex);
            }

            // sort timestamp desc
            orders.Sort((o1, o2) =>
            {
                return o2.OrderDate.CompareTo(o1.OrderDate);
            });
            return orders;
        }

        protected override async Task<IEnumerable<ExchangeOrderResult>> OnGetCompletedOrderDetailsAsync(string symbol = null, DateTime? afterDate = null)
        {
            List<ExchangeOrderResult> orders = new List<ExchangeOrderResult>();
            if (string.IsNullOrWhiteSpace(symbol))
            {
                orders.AddRange(await GetCompletedOrdersForAllSymbolsAsync(afterDate));
            }
            else
            {
                Dictionary<string, object> payload = GetNoncePayload();
                payload["symbol"] = NormalizeSymbol(symbol);
                if (afterDate != null)
                {
                    // TODO: timestamp param is causing duplicate request errors which is a bug in the Binance API
                    // payload["timestamp"] = afterDate.Value.UnixTimestampFromDateTimeMilliseconds();
                }
                JToken token = await MakeJsonRequestAsync<JToken>("/allOrders", BaseUrlPrivate, payload);
                CheckError(token);
                foreach (JToken order in token)
                {
                    orders.Add(ParseOrder(order));
                }
            }
            return orders;
        }

        protected override async Task OnCancelOrderAsync(string orderId, string symbol = null)
        {
            Dictionary<string, object> payload = GetNoncePayload();
            if (string.IsNullOrEmpty(symbol))
            {
                throw new InvalidOperationException("Binance cancel order request requires symbol");
            }
            payload["symbol"] = NormalizeSymbol(symbol);
            payload["orderId"] = orderId;
            JToken token = await MakeJsonRequestAsync<JToken>("/order", BaseUrlPrivate, payload, "DELETE");
            CheckError(token);
        }

        /// <summary>A withdrawal request. Fee is automatically subtracted from the amount.</summary>
        /// <param name="withdrawalRequest">The withdrawal request.</param>
        /// <returns>Withdrawal response from Binance</returns>
        protected override async Task<ExchangeWithdrawalResponse> OnWithdrawAsync(ExchangeWithdrawalRequest withdrawalRequest)
        {
            if (string.IsNullOrWhiteSpace(withdrawalRequest.Symbol))
            {
                throw new ArgumentException("Symbol must be provided for Withdraw");
            }
            else if (string.IsNullOrWhiteSpace(withdrawalRequest.Address))
            {
                throw new ArgumentException("Address must be provided for Withdraw");
            }
            else if (withdrawalRequest.Amount <= 0)
            {
                throw new ArgumentException("Withdrawal amount must be positive and non-zero");
            }

            Dictionary<string, object> payload = GetNoncePayload();
            payload["asset"] = withdrawalRequest.Symbol;
            payload["address"] = withdrawalRequest.Address;
            payload["amount"] = withdrawalRequest.Amount;
            payload["name"] = withdrawalRequest.Description ?? "apiwithdrawal"; // Contrary to what the API docs say, name is required

            if (!string.IsNullOrWhiteSpace(withdrawalRequest.AddressTag))
            {
                payload["addressTag"] = withdrawalRequest.AddressTag;
            }

            JToken response = await MakeJsonRequestAsync<JToken>("/withdraw.html", WithdrawalUrlPrivate, payload, "POST");
            CheckError(response);

            ExchangeWithdrawalResponse withdrawalResponse = new ExchangeWithdrawalResponse
            {
                Id = response["id"].ToStringInvariant(),
                Message = response["msg"].ToStringInvariant(),
            };

            return withdrawalResponse;
        }

        private bool ParseMarketStatus(string status)
        {
            bool isActive = false;
            if (!string.IsNullOrWhiteSpace(status))
            {
                switch (status)
                {
                    case "TRADING":
                    case "PRE_TRADING":
                    case "POST_TRADING":
                        isActive = true;
                        break;
                        /* case "END_OF_DAY":
                            case "HALT":
                            case "AUCTION_MATCH":
                            case "BREAK": */
                }
            }

            return isActive;
        }

        private void CheckError(JToken result)
        {
            if (result != null && !(result is JArray))
            {
                if (result["status"] != null && result["code"] != null)
                {
                    throw new APIException("Code: " + result["code"].ToStringInvariant() + ", error: " + (result["msg"] != null ? result["msg"].ToStringInvariant() : "Unknown Error"));
                }
                else if (result["success"] != null && !result["success"].ConvertInvariant<bool>())
                {
                    throw new APIException("Success was false, error: " + result["msg"].ToStringInvariant());
                }
            }
        }

        private ExchangeTicker ParseTicker(string symbol, JToken token)
        {
            // {"priceChange":"-0.00192300","priceChangePercent":"-4.735","weightedAvgPrice":"0.03980955","prevClosePrice":"0.04056700","lastPrice":"0.03869000","lastQty":"0.69300000","bidPrice":"0.03858500","bidQty":"38.35000000","askPrice":"0.03869000","askQty":"31.90700000","openPrice":"0.04061300","highPrice":"0.04081900","lowPrice":"0.03842000","volume":"128015.84300000","quoteVolume":"5096.25362239","openTime":1512403353766,"closeTime":1512489753766,"firstId":4793094,"lastId":4921546,"count":128453}
            return new ExchangeTicker
            {
                Ask = token["askPrice"].ConvertInvariant<decimal>(),
                Bid = token["bidPrice"].ConvertInvariant<decimal>(),
                Last = token["lastPrice"].ConvertInvariant<decimal>(),
                Volume = new ExchangeVolume
                {
                    BaseVolume = token["volume"].ConvertInvariant<decimal>(),
                    BaseSymbol = symbol,
                    ConvertedVolume = token["quoteVolume"].ConvertInvariant<decimal>(),
                    ConvertedSymbol = symbol,
                    Timestamp = CryptoUtility.UnixTimeStampToDateTimeMilliseconds(token["closeTime"].ConvertInvariant<long>())
                }
            };
        }

        private ExchangeTicker ParseTickerWebSocket(JToken token)
        {
            return new ExchangeTicker
            {
                Ask = token["a"].ConvertInvariant<decimal>(),
                Bid = token["b"].ConvertInvariant<decimal>(),
                Last = token["c"].ConvertInvariant<decimal>(),
                Volume = new ExchangeVolume
                {
                    BaseVolume = token["v"].ConvertInvariant<decimal>(),
                    BaseSymbol = token["s"].ToStringInvariant(),
                    ConvertedVolume = token["q"].ConvertInvariant<decimal>(),
                    ConvertedSymbol = token["s"].ToStringInvariant(),
                    Timestamp = CryptoUtility.UnixTimeStampToDateTimeMilliseconds(token["E"].ConvertInvariant<long>())
                }
            };
        }

        private ExchangeOrderBook ParseOrderBook(JToken token)
        {
            ExchangeOrderBook book = new ExchangeOrderBook();
            foreach (JArray array in token["bids"])
            {
                book.Bids.Add(new ExchangeOrderPrice { Price = array[0].ConvertInvariant<decimal>(), Amount = array[1].ConvertInvariant<decimal>() });
            }
            foreach (JArray array in token["asks"])
            {
                book.Asks.Add(new ExchangeOrderPrice { Price = array[0].ConvertInvariant<decimal>(), Amount = array[1].ConvertInvariant<decimal>() });
            }
            return book;
        }

        private ExchangeOrderResult ParseOrder(JToken token)
        {
            /*
              "symbol": "IOTABTC",
              "orderId": 1,
              "clientOrderId": "12345",
              "transactTime": 1510629334993,
              "price": "1.00000000",
              "origQty": "1.00000000",
              "executedQty": "0.00000000",
              "status": "NEW",
              "timeInForce": "GTC",
              "type": "LIMIT",
              "side": "SELL",
              "fills": [
                  {
                    "price": "4000.00000000",
                    "qty": "1.00000000",
                    "commission": "4.00000000",
                    "commissionAsset": "USDT"
                  },
                  {
                    "price": "3999.00000000",
                    "qty": "5.00000000",
                    "commission": "19.99500000",
                    "commissionAsset": "USDT"
                  },
                  {
                    "price": "3998.00000000",
                    "qty": "2.00000000",
                    "commission": "7.99600000",
                    "commissionAsset": "USDT"
                  },
                  {
                    "price": "3997.00000000",
                    "qty": "1.00000000",
                    "commission": "3.99700000",
                    "commissionAsset": "USDT"
                  },
                  {
                    "price": "3995.00000000",
                    "qty": "1.00000000",
                    "commission": "3.99500000",
                    "commissionAsset": "USDT"
                  }
                ]
            */
            ExchangeOrderResult result = new ExchangeOrderResult
            {
                Amount = token["origQty"].ConvertInvariant<decimal>(),
                AmountFilled = token["executedQty"].ConvertInvariant<decimal>(),
                Price = token["price"].ConvertInvariant<decimal>(),
                IsBuy = token["side"].ToStringInvariant() == "BUY",
                OrderDate = CryptoUtility.UnixTimeStampToDateTimeMilliseconds(token["time"].ConvertInvariant<long>(token["transactTime"].ConvertInvariant<long>())),
                OrderId = token["orderId"].ToStringInvariant(),
                Symbol = token["symbol"].ToStringInvariant()
            };
            result.AveragePrice = result.Price;
            switch (token["status"].ToStringInvariant())
            {
                case "NEW":
                    result.Result = ExchangeAPIOrderResult.Pending;
                    break;

                case "PARTIALLY_FILLED":
                    result.Result = ExchangeAPIOrderResult.FilledPartially;
                    break;

                case "FILLED":
                    result.Result = ExchangeAPIOrderResult.Filled;
                    break;

                case "CANCELED":
                case "PENDING_CANCEL":
                case "EXPIRED":
                case "REJECTED":
                    result.Result = ExchangeAPIOrderResult.Canceled;
                    break;

                default:
                    result.Result = ExchangeAPIOrderResult.Error;
                    break;
            }

            ParseFeesFromFills(result, token["fills"]);

            return result;
        }

        private void ParseFeesFromFills(ExchangeOrderResult result, JToken fillsToken)
        {
            bool currencySet = false;
            if (fillsToken is JArray)
            {
                foreach (var fill in fillsToken)
                {
                    if (!currencySet)
                    {
                        result.FeesCurrency = fill["commissionAsset"].ToStringInvariant();
                        currencySet = true;
                    }

                    result.Fees += fill["commission"].ConvertInvariant<decimal>();
                }
            }
        }

        protected override void ProcessRequest(HttpWebRequest request, Dictionary<string, object> payload)
        {
            if (CanMakeAuthenticatedRequest(payload))
            {
                request.Headers["X-MBX-APIKEY"] = PublicApiKey.ToUnsecureString();
            }
        }

        protected override Uri ProcessRequestUrl(UriBuilder url, Dictionary<string, object> payload)
        {
            if (CanMakeAuthenticatedRequest(payload))
            {
                // payload is ignored, except for the nonce which is added to the url query - bittrex puts all the "post" parameters in the url query instead of the request body
                var query = HttpUtility.ParseQueryString(url.Query);
                string newQuery = "timestamp=" + payload["nonce"].ToStringInvariant() + (query.Count == 0 ? string.Empty : "&" + query.ToString()) +
                    (payload.Count > 1 ? "&" + GetFormForPayload(payload, false) : string.Empty);
                string signature = CryptoUtility.SHA256Sign(newQuery, CryptoUtility.SecureStringToBytes(PrivateApiKey));
                newQuery += "&signature=" + signature;
                url.Query = newQuery;
                return url.Uri;
            }
            return base.ProcessRequestUrl(url, payload);
        }

        /// <summary>
        /// Gets the address to deposit to and applicable details.
        /// </summary>
        /// <param name="symbol">Symbol to get address for</param>
        /// <param name="forceRegenerate">(ignored) Binance does not provide the ability to generate new addresses</param>
        /// <returns>
        /// Deposit address details (including tag if applicable, such as XRP)
        /// </returns>
        protected override async Task<ExchangeDepositDetails> OnGetDepositAddressAsync(string symbol, bool forceRegenerate = false)
        {
            /* 
            * TODO: Binance does not offer a "regenerate" option in the API, but a second IOTA deposit to the same address will not be credited
            * How does Binance handle GetDepositAddress for IOTA after it's been used once?
            * Need to test calling this API after depositing IOTA.
            */

            Dictionary<string, object> payload = GetNoncePayload();
            payload["asset"] = NormalizeSymbol(symbol);

            JToken response = await MakeJsonRequestAsync<JToken>("/depositAddress.html", WithdrawalUrlPrivate, payload);
            CheckError(response);

            ExchangeDepositDetails depositDetails = new ExchangeDepositDetails
            {
                Symbol = response["asset"].ToStringInvariant(),
                Address = response["address"].ToStringInvariant(),
                AddressTag = response["addressTag"].ToStringInvariant()
            };

            return depositDetails;
        }

        /// <summary>Gets the deposit history for a symbol</summary>
        /// <param name="symbol">The symbol to check. Null for all symbols.</param>
        /// <returns>Collection of ExchangeCoinTransfers</returns>
        protected override async Task<IEnumerable<ExchangeTransaction>> OnGetDepositHistoryAsync(string symbol)
        {
            // TODO: API supports searching on status, startTime, endTime
            Dictionary<string, object> payload = GetNoncePayload();
            if (!string.IsNullOrWhiteSpace(symbol))
            {
                payload["asset"] = NormalizeSymbol(symbol);
            }

            JToken response = await MakeJsonRequestAsync<JToken>("/depositHistory.html", WithdrawalUrlPrivate, payload);
            CheckError(response);

            var transactions = new List<ExchangeTransaction>();
            foreach (JToken token in response["depositList"])
            {
                var transaction = new ExchangeTransaction
                {
                    TimestampUTC = token["insertTime"].ConvertInvariant<double>().UnixTimeStampToDateTimeMilliseconds(),
                    Amount = token["amount"].ConvertInvariant<decimal>(),
                    Symbol = token["asset"].ToStringUpperInvariant(),
                    Address = token["address"].ToStringInvariant(),
                    AddressTag = token["addressTag"].ToStringInvariant(),
                    BlockchainTxId = token["txId"].ToStringInvariant()
                };
                int status = token["status"].ConvertInvariant<int>();
                switch (status)
                {
                    case 0:
                        transaction.Status = TransactionStatus.Processing;
                        break;

                    case 1:
                        transaction.Status = TransactionStatus.Complete;
                        break;

                    default:
                        // If new states are added, see https://github.com/binance-exchange/binance-official-api-docs/blob/master/wapi-api.md
                        transaction.Status = TransactionStatus.Unknown;
                        transaction.Notes = "Unknown transaction status: " + status;
                        break;
                }

                transactions.Add(transaction);
            }

            return transactions;
        }
    }
}
