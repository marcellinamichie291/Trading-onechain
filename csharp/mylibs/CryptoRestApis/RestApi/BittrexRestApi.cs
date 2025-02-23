﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CryptoRestApis.RestApi.Models;
using Bittrex.Net;
using CryptoTools.Net;
using static CryptoTools.General.General;

namespace CryptoRestApis.RestApi
{
    // https://www.nuget.org/packages/Bittrex.Net/

    public class BittrexRestApi : ICryptoRestApi
    {
        private BittrexClient m_client;

        public BittrexRestApi(string apiKey, string apiSecret)
        {
            var options = new Bittrex.Net.Objects.BittrexClientOptions();
            options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(apiKey, apiSecret);
            m_client = new BittrexClient(options);
        }

        // This TEST method should show sample code for each of the following:
        // method: Get Account Balances (for each currency), Get Deposit Addresses (for each currency)
        // method: Get Deposit History, Get Withdrawal History
        // method: Withdraw (to cryptoAddress)
        public static void Test()
        {
            // API test code goes here
        }

        #region ---------- ICryptoApi ---------------------------------------------------------------
        public string Exchange { get { return "BITTREX"; } }

        public List<string> GetAllSymbols()
        {
            var result = new List<string>();
            var res = m_client.GetMarketSummaries();
            foreach (var p in res.Data)
            {
                result.Add(p.MarketName);
            }
            return result;
        }

        public string GetSymbol(string symbolId)
        {
            if (symbolId == "btcusdt") return "USDT-BTC";
            else if (symbolId == "ethusdt") return "USDT-ETH";
            else if (symbolId == "ethbtc") return "BTC-ETH";
            else
            {
                Console.WriteLine("ERROR: Symbol ID not found.");
                return null;
            }
        }

        public async Task<XTicker> GetTicker(string symbolId)
        {
            string symbol = GetSymbol(symbolId);
            var res = await m_client.GetTickerAsync(symbol);
            return new XTicker(res.Data);
        }

        public async Task<XBalanceMap> GetBalances()
        {
            var res = await m_client.GetBalancesAsync();
            return new XBalanceMap(res.Data);
        }
        #endregion ----------------------------------------------------------------------------------


    } // end of class BinanceRestApi
} // end of namespace

