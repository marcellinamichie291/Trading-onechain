﻿/*
MIT LICENSE

Copyright 2017 Digital Ruby, LLC - http://www.digitalruby.com

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web;

using Newtonsoft.Json.Linq;

namespace ExchangeSharp
{ 

    public sealed class ExchangeBleutradeAPI : ExchangeAPI
    {
        public override string Name => ExchangeName.Bleutrade;
        public override string BaseUrl { get; set; } = "https://bleutrade.com/api/v2";

        static ExchangeBleutradeAPI()
        {
            ExchangeGlobalCurrencyReplacements[typeof(ExchangeBleutradeAPI)] = new KeyValuePair<string, string>[]
            {
                new KeyValuePair<string, string>("BCC", "BCH")
            };
        }

        public ExchangeBleutradeAPI()
        {
            NonceStyle = NonceStyle.UnixMillisecondsString;
            SymbolSeparator = "_";
            SymbolIsReversed = true;
        }

        public override string NormalizeSymbol(string symbol)
        {
            return (symbol ?? string.Empty).Replace('/', '_').Replace('-', '_');
        }

        #region ProcessRequest 

        protected override void ProcessRequest(HttpWebRequest request, Dictionary<string, object> payload)
        {
            if (CanMakeAuthenticatedRequest(payload))
            {
                request.Headers["apisign"] = CryptoUtility.SHA512Sign(request.RequestUri.ToString(), PrivateApiKey.ToUnsecureString()).ToLower();
            }
        }

        protected override Uri ProcessRequestUrl(UriBuilder url, Dictionary<string, object> payload)
        {
            if (CanMakeAuthenticatedRequest(payload))
            {
                // payload is ignored, except for the nonce which is added to the url query
                var query = HttpUtility.ParseQueryString(url.Query);
                url.Query = "apikey=" + PublicApiKey.ToUnsecureString() + "&nonce=" + payload["nonce"].ToStringInvariant() + (query.Count == 0 ? string.Empty : "&" + query.ToString());
            }
            return url.Uri;
        }

        #endregion

        #region Public APIs

        protected override async Task<IReadOnlyDictionary<string, ExchangeCurrency>> OnGetCurrenciesAsync()
        {
            var currencies = new Dictionary<string, ExchangeCurrency>(StringComparer.OrdinalIgnoreCase);
            //{ "success" : true,"message" : "", "result" : [{"Currency" : "BTC","CurrencyLong" : "Bitcoin","MinConfirmation" : 2,"TxFee" : 0.00080000,"IsActive" : true, "CoinType" : "BITCOIN","MaintenanceMode" : false}, ... 
            JToken result = await MakeJsonRequestAsync<JToken>("/public/getcurrencies", null, null);
            result = CheckError(result);
            foreach (JToken token in result)
            {
                var coin = new ExchangeCurrency
                {
                    CoinType = token["CoinType"].ToStringInvariant(),
                    FullName = token["CurrencyLong"].ToStringInvariant(),
                    IsEnabled = token["MaintenanceMode"].ConvertInvariant<bool>().Equals(false),
                    MinConfirmations = token["MinConfirmation"].ConvertInvariant<int>(),
                    Name = token["Currency"].ToStringUpperInvariant(),
                    Notes = token["Notice"].ToStringInvariant(),
                   TxFee = token["TxFee"].ConvertInvariant<decimal>(),
                };
                currencies[coin.Name] = coin;
            }
            return currencies;
        }

        protected override async Task<IEnumerable<string>> OnGetSymbolsAsync()
        {
            List<string> symbols = new List<string>();
            JToken result = await MakeJsonRequestAsync<JToken>("/public/getmarkets", null, null);
            result = CheckError(result);
            foreach (var market in result) symbols.Add(market["MarketName"].ToStringInvariant());
            return symbols;
        }

        protected override async Task<IEnumerable<ExchangeMarket>> OnGetSymbolsMetadataAsync()
        {
            List<ExchangeMarket> markets = new List<ExchangeMarket>();
            // "result" : [{"MarketCurrency" : "DOGE","BaseCurrency" : "BTC","MarketCurrencyLong" : "Dogecoin","BaseCurrencyLong" : "Bitcoin", "MinTradeSize" : 0.10000000, "MarketName" : "DOGE_BTC", "IsActive" : true, }, ...
            JToken result = await MakeJsonRequestAsync<JToken>("/public/getmarkets", null, null);
            result = CheckError(result);
            foreach (JToken token in result)
            {
                markets.Add(new ExchangeMarket()
                {
                     MarketName = token["MarketName"].ToStringInvariant(),
                     BaseCurrency = token["BaseCurrency"].ToStringInvariant(),
                     MarketCurrency = token["MarketCurrency"].ToStringInvariant(),
                     IsActive = token["IsActive"].ToStringInvariant().Equals("true"),
                     MinTradeSize = token["MinTradeSize"].ConvertInvariant<decimal>(),
                });
            }
            return markets;
        }

        protected override async Task<ExchangeTicker> OnGetTickerAsync(string symbol)
        {
            JToken result = await MakeJsonRequestAsync<JObject>("/public/getmarketsummary?market=" + symbol);
            result = CheckError(result);
            result = result[0];
            return new ExchangeTicker
            {
                Ask = result["Ask"].ConvertInvariant<decimal>(),
                Bid = result["Bid"].ConvertInvariant<decimal>(),
                Last = result["Last"].ConvertInvariant<decimal>(),
                Volume = new ExchangeVolume
                {
                    BaseVolume = result["Volume"].ConvertInvariant<decimal>(),
                    BaseSymbol = result["BaseCurrency"].ToStringInvariant(),
                    ConvertedVolume = result["BaseVolume"].ConvertInvariant<decimal>(),
                    ConvertedSymbol = result["MarketCurrency"].ToStringInvariant(),
                    Timestamp = result["TimeStamp"].ConvertInvariant<DateTime>()
                }
            };
        }

        protected override async Task<IEnumerable<KeyValuePair<string, ExchangeTicker>>> OnGetTickersAsync()
        {
            List<KeyValuePair<string, ExchangeTicker>> tickers = new List<KeyValuePair<string, ExchangeTicker>>();
            // "result" : [{"MarketCurrency" : "Ethereum","BaseCurrency" : "Bitcoin","MarketName" : "ETH_BTC","PrevDay" : 0.00095000,"High" : 0.00105000,"Low" : 0.00086000, "Last" : 0.00101977, "Average" : 0.00103455, "Volume" : 2450.97496015, "BaseVolume" : 2.40781647,    "TimeStamp" : "2014-07-29 11:19:30", "Bid" : 0.00100000, "Ask" : 0.00101977, "IsActive" : true }, ... ]
            JToken result = await MakeJsonRequestAsync<JObject>("/public/getmarketsummaries");
            result = CheckError(result);
            foreach (JToken token in result)
            {
                var ticker = new ExchangeTicker
                {
                    Ask = token["Ask"].ConvertInvariant<decimal>(),
                    Bid = token["Bid"].ConvertInvariant<decimal>(),
                    Last = token["Last"].ConvertInvariant<decimal>(),
                    Volume = new ExchangeVolume()
                    {
                        Timestamp = token["TimeStamp"].ConvertInvariant<DateTime>(),
                        BaseSymbol = token["BaseCurrency"].ToStringInvariant(),
                        BaseVolume = token["BaseVolume"].ConvertInvariant<decimal>(),
                        ConvertedSymbol = token["MarketCurrency"].ToStringInvariant(),
                        ConvertedVolume = token["Volume"].ConvertInvariant<decimal>()
                    }
                };
                tickers.Add(new  KeyValuePair<string, ExchangeTicker>(token["MarketName"].ToStringInvariant(), ticker));
            }
            return tickers;
        }

        protected override async Task<IEnumerable<MarketCandle>> OnGetCandlesAsync(string symbol, int periodSeconds, DateTime? startDate = null, DateTime? endDate = null, int? limit = null)
        {
            List<MarketCandle> candles = new List<MarketCandle>();
            string periodString;
            if (periodSeconds <= 900) { periodString = "15m"; periodSeconds = 900; }
            else if (periodSeconds <= 1200) { periodString = "20m"; periodSeconds = 1200; }
            else if (periodSeconds <= 1800) { periodString = "30m"; periodSeconds = 1800; }
            else if (periodSeconds <= 3600) { periodString = "1h"; periodSeconds = 3600; }
            else if (periodSeconds <= 7200) { periodString = "2h"; periodSeconds = 7200; }
            else if (periodSeconds <= 10800) { periodString = "3h"; periodSeconds = 10800; }
            else if (periodSeconds <= 14400) { periodString = "4h"; periodSeconds = 14400; }
            else if (periodSeconds <= 21600) { periodString = "6h"; periodSeconds = 21600; }
            else if (periodSeconds <= 43200) { periodString = "12h"; periodSeconds = 43200; }
            else { periodString = "1d"; periodSeconds = 86400; }

            limit = limit ?? (limit > 2160 ? 2160 : limit);
            symbol = NormalizeSymbol(symbol);
            endDate = endDate ?? DateTime.UtcNow;
            startDate = startDate ?? endDate.Value.Subtract(TimeSpan.FromDays(1.0));

            //market period(15m, 20m, 30m, 1h, 2h, 3h, 4h, 6h, 8h, 12h, 1d) count(default: 1000, max: 999999) lasthours(default: 24, max: 2160) 
            //"result":[{"TimeStamp":"2014-07-31 10:15:00","Open":"0.00000048","High":"0.00000050","Low":"0.00000048","Close":"0.00000049","Volume":"594804.73036048","BaseVolume":"0.11510368" }, ...
            JToken result = await MakeJsonRequestAsync<JObject>("/public/getcandles?market=" + symbol + "&period=" + periodString + (limit == null ? string.Empty : "&lasthours=" + limit));
            result = CheckError(result);
                foreach (JToken jsonCandle in result)
                {
                    MarketCandle candle = new MarketCandle
                    {
                        ExchangeName = this.Name,
                        Name = symbol,
                        Timestamp = jsonCandle["TimeStamp"].ConvertInvariant<DateTime>(),
                        OpenPrice = jsonCandle["Open"].ConvertInvariant<decimal>(),
                        HighPrice = jsonCandle["High"].ConvertInvariant<decimal>(),
                        LowPrice = jsonCandle["Low"].ConvertInvariant<decimal>(),
                        ClosePrice = jsonCandle["Close"].ConvertInvariant<decimal>(),
                        PeriodSeconds = periodSeconds,
                        BaseVolume = jsonCandle["BaseVolume"].ConvertInvariant<double>(),
                        ConvertedVolume = jsonCandle["Volume"].ConvertInvariant<double>()
                    };
                    if (candle.Timestamp >= startDate && candle.Timestamp <= endDate) candles.Add(candle);
                }
            return candles;
        }


        protected override async Task<IEnumerable<ExchangeTrade>> OnGetRecentTradesAsync(string symbol)
        {
            List<ExchangeTrade> trades = new List<ExchangeTrade>();
            //"result" : [{ "TimeStamp" : "2014-07-29 18:08:00","Quantity" : 654971.69417461,"Price" : 0.00000055,"Total" : 0.360234432,"OrderType" : "BUY"}, ...  ]
            JToken result = await MakeJsonRequestAsync<JObject>("/public/getmarkethistory?market=" + symbol);
            result = CheckError(result);
            foreach(JToken token in result) trades.Add(ParseTrade(token));
            return trades;
        }

        protected override async Task OnGetHistoricalTradesAsync(Func<IEnumerable<ExchangeTrade>, bool> callback, string symbol, DateTime? sinceDateTime = null)
        {
            List<ExchangeTrade> trades = new List<ExchangeTrade>();
            // TODO: Not directly supported so the best we can do is get their Max 200 and check the timestamp if necessary
            JToken result = await MakeJsonRequestAsync<JObject>("/public/getmarkethistory?market=" + symbol + "&count=200");
            result = CheckError(result);
            foreach (JToken token in result)
            {
                ExchangeTrade trade = ParseTrade(token);
                if (sinceDateTime == null || trade.Timestamp >= sinceDateTime)
                {
                    trades.Add(trade);
                }
            }
            if (trades.Count != 0)
            {
                callback(trades);
            }
        }

        protected override async Task<ExchangeOrderBook> OnGetOrderBookAsync(string symbol, int maxCount = 100)
        {
            ExchangeOrderBook orders = new ExchangeOrderBook();
            //"result" : { "buy" : [{"Quantity" : 4.99400000,"Rate" : 3.00650900}, {"Quantity" : 50.00000000, "Rate" : 3.50000000 }  ] ...
            JToken result = await MakeJsonRequestAsync<JObject>("/public/getorderbook?market=" + symbol + "&type=ALL&depth=" + maxCount);
            result = CheckError(result);
            foreach (JToken token in result["buy"]) if (orders.Bids.Count < maxCount) orders.Bids.Add(new ExchangeOrderPrice() { Amount = token["Quantity"].ConvertInvariant<decimal>(), Price = token["Rate"].ConvertInvariant<decimal>() });
            foreach (JToken token in result["sell"]) if (orders.Asks.Count < maxCount) orders.Asks.Add(new ExchangeOrderPrice() { Amount = token["Quantity"].ConvertInvariant<decimal>(), Price = token["Rate"].ConvertInvariant<decimal>() });
            return orders;
        }

        #endregion

        # region Private APIs

        protected override async Task<Dictionary<string, decimal>> OnGetAmountsAsync()
        {
            Dictionary<string, decimal> amounts = new Dictionary<string, decimal>();
            // "result" : [{"Currency" : "DOGE","Balance" : 0.00000000,"Available" : 0.00000000,"Pending" : 0.00000000,"CryptoAddress" : "DBSwFELQiVrwxFtyHpVHbgVrNJXwb3hoXL", "IsActive" : true}, ... 
            JToken result = await MakeJsonRequestAsync<JToken>("/account/getbalances", null, GetNoncePayload());
            result = CheckError(result);
            foreach (JToken token in result)
            {
                decimal amount = result["Balance"].ConvertInvariant<decimal>();
                if (amount > 0) amounts[token["Currency"].ToStringInvariant()] = amount;
            }
            return amounts;
        }

        protected override async Task<Dictionary<string, decimal>> OnGetAmountsAvailableToTradeAsync()
        {
            Dictionary<string, decimal> amounts = new Dictionary<string, decimal>();
            // "result" : [{"Currency" : "DOGE","Balance" : 0.00000000,"Available" : 0.00000000,"Pending" : 0.00000000,"CryptoAddress" : "DBSwFELQiVrwxFtyHpVHbgVrNJXwb3hoXL", "IsActive" : true}, ... 
            JToken result = await MakeJsonRequestAsync<JToken>("/account/getbalances", null, GetNoncePayload());
            result = CheckError(result);
            foreach (JToken token in result)
            {
                decimal amount = result["Available"].ConvertInvariant<decimal>();
                if (amount > 0) amounts[token["Currency"].ToStringInvariant()] = amount;
            }
            return amounts;
        }

        protected override async Task<ExchangeOrderResult> OnGetOrderDetailsAsync(string orderId, string symbol = null)
        {
            // "result" : { "OrderId" : "65489","Exchange" : "LTC_BTC", "Type" : "BUY", "Quantity" : 20.00000000, "QuantityRemaining" : 5.00000000, "QuantityBaseTraded" : "0.16549400", "Price" : 0.01268311, "Status" : "OPEN", "Created" : "2014-08-03 13:55:20", "Comments" : "My optional comment, eg function id #123"  }
            JToken result = await MakeJsonRequestAsync<JToken>("/account/getorder?orderid=" + orderId , null, GetNoncePayload());
            result = CheckError(result);
            return ParseOrder(result);
        }

        protected override async Task<IEnumerable<ExchangeOrderResult>> OnGetCompletedOrderDetailsAsync(string symbol = null, DateTime? afterDate = null)
        {
            List<ExchangeOrderResult> orders = new List<ExchangeOrderResult>();
            JToken result = await MakeJsonRequestAsync<JToken>("/account/getorders?market=" + (string.IsNullOrEmpty(symbol) ? "ALL" : symbol) + "&orderstatus=OK&ordertype=ALL", null, GetNoncePayload());
            result = CheckError(result);
            foreach (JToken token in result)
            {
                ExchangeOrderResult order = ParseOrder(token);
                if (afterDate != null) { if (order.OrderDate > afterDate) orders.Add(order); }
                else orders.Add(order);
            }
            return orders;
        }

        protected override async Task<IEnumerable<ExchangeOrderResult>> OnGetOpenOrderDetailsAsync(string symbol = null)
        {
            List<ExchangeOrderResult> orders = new List<ExchangeOrderResult>();
            JToken result = await MakeJsonRequestAsync<JToken>("/market/getopenorders", null, GetNoncePayload());
            result = CheckError(result);
            foreach (JToken token in result) orders.Add(ParseOrder(token));
            return orders;
        }

        protected override async Task<ExchangeOrderResult> OnPlaceOrderAsync(ExchangeOrderRequest order)
        {
            ExchangeOrderResult result = new ExchangeOrderResult() { Result = ExchangeAPIOrderResult.Error };
            // Only limit order is supported - no indication on how it is filled
            JToken token = await MakeJsonRequestAsync<JToken>((order.IsBuy ? "/market/buylimit?" : "market/selllimit?") + "market=" + order.Symbol + "&rate=" + order.Price + "&quantity=" + order.Amount, null, GetNoncePayload());
            token = CheckError(token);
            if (token.HasValues)
            {
                // Only the orderid is returned on success
                result.OrderId = token["orderid"].ToStringInvariant();
                result.Result = ExchangeAPIOrderResult.Filled;
            }
            return result;
        }

        protected override async Task OnCancelOrderAsync(string orderId, string symbol = null)
        {
            JToken result = await MakeJsonRequestAsync<JToken>("/market/cancel?orderid=" + orderId, null, GetNoncePayload());
            CheckError(result);
        }

        protected override async Task<ExchangeDepositDetails> OnGetDepositAddressAsync(string symbol, bool forceRegenerate = false)
        {
            JToken token = await MakeJsonRequestAsync<JToken>("/account/getdepositaddress?" + "currency=" + NormalizeSymbol(symbol), BaseUrl, GetNoncePayload(), "GET");
            token = CheckError(token);
            if (token["Currency"].ToStringInvariant().Equals(symbol) && token["Address"] != null)
            {
                // At this time, according to Bleutrade support, they don't support any currency requiring an Address Tag, but they will add this feature in the future
                return new ExchangeDepositDetails()
                {
                     Symbol = token["Currency"].ToStringInvariant(),
                     Address = token["Address"].ToStringInvariant()
                 };
            }
            return null;
        }

        protected override async Task<IEnumerable<ExchangeTransaction>> OnGetDepositHistoryAsync(string symbol)
        {
            List<ExchangeTransaction> transactions = new List<ExchangeTransaction>();

            // "result" : [{"Id" : "44933431","TimeStamp" : "2015-05-13 07:15:23","Coin" : "LTC","Amount" : -0.10000000,"Label" : "Withdraw: 0.99000000 to address Anotheraddress; fee 0.01000000","TransactionId" : "c396228895f8976e3810286c1537bddd4a45bb37d214c0e2b29496a4dee9a09b" }
            JToken result = await MakeJsonRequestAsync<JToken>("/account/getdeposithistory", BaseUrl, GetNoncePayload(), "GET");
            result = CheckError(result);
            foreach (JToken token in result)
            {
                transactions.Add(new ExchangeTransaction()
                {
                     PaymentId = token["Id"].ToStringInvariant(),
                     BlockchainTxId = token["TransactionId"].ToStringInvariant(),
                     TimestampUTC = token["TimeStamp"].ConvertInvariant<DateTime>(),
                     Symbol = token["Coin"].ToStringInvariant(),
                     Amount = token["Amount"].ConvertInvariant<decimal>(),
                     Notes = token["Label"].ToStringInvariant(),
                     TxFee = token["fee"].ConvertInvariant<decimal>(),
                     Status = TransactionStatus.Unknown
                });
            }
            return transactions;
        }

        protected override async Task<ExchangeWithdrawalResponse> OnWithdrawAsync(ExchangeWithdrawalRequest withdrawalRequest)
        {
            var payload = GetNoncePayload();
            payload["currency"] = NormalizeSymbol(withdrawalRequest.Symbol);
            payload["quantity"] = withdrawalRequest.Amount;
            payload["address"] = withdrawalRequest.Address;
            if (!string.IsNullOrEmpty(withdrawalRequest.AddressTag)) payload["comments"] = withdrawalRequest.AddressTag;
            
            JToken token = await MakeJsonRequestAsync<JToken>("/account/withdraw", BaseUrl, payload, "GET");
            CheckError(token);
            // Bleutrade doesn't return any info, just an empty string on success. The CheckError will throw an exception if there's an error
            return new ExchangeWithdrawalResponse() { Success = true };
        }


        #endregion

        #region Private Functions

        private JToken CheckError(JToken result)
        {
            if (result == null || (result["success"] != null && result["success"].Value<bool>() != true))
            {
                throw new APIException((result["message"] != null ? result["message"].Value<string>() : "Unknown Error"));
            }
            return result["result"];
        }

        private ExchangeTrade ParseTrade(JToken token)
        {
            return new ExchangeTrade()
            {
                Timestamp = token["TimeStamp"].ConvertInvariant<DateTime>(),
                IsBuy = token["OrderType"].ToStringInvariant().Equals("BUY"),
                Price = token["Price"].ConvertInvariant<decimal>(),
                Amount = token["Quantity"].ConvertInvariant<decimal>()
            };
        }


        private ExchangeOrderResult ParseOrder(JToken token)
        {
            var order = new ExchangeOrderResult()
            {
                OrderId = token["OrderId"].ToStringInvariant(),
                IsBuy = token["Type"].ToStringInvariant().Equals("BUY"),
                Symbol = token["Exchange"].ToStringInvariant(),
                Amount = token["Quantity"].ConvertInvariant<decimal>(),
                OrderDate = token["Created"].ConvertInvariant<DateTime>(),
                AveragePrice = token["Price"].ConvertInvariant<decimal>(),
                AmountFilled = token["QuantityBaseTraded"].ConvertInvariant<decimal>(),
                Message = token["Comments"].ToStringInvariant(),
            };

            switch (token["status"].ToStringInvariant())
            {
                case "OPEN":
                    order.Result = ExchangeAPIOrderResult.Pending;
                    break;
                case "OK":
                    if (order.Amount == order.AmountFilled) order.Result = ExchangeAPIOrderResult.Filled;
                    else order.Result = ExchangeAPIOrderResult.FilledPartially;
                    break;
                case "CANCELED":
                    order.Result = ExchangeAPIOrderResult.Canceled;
                    break;
                default:
                    order.Result = ExchangeAPIOrderResult.Unknown;
                    break;
            }
            return order;
        }

        #endregion

    }
}
