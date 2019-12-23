#region Using Directives
using System;
using System.Reflection;
using Xunit;
using TrinanceLib;
#endregion

namespace TrinanceTests
{
    public class Tests
    {
        [Theory]
        [InlineData("BTC-STX-BNB", 0.0341d, 0.0150d)]
        [InlineData("BTC-TFUEL-BNB", 0.1126d, 0.0150d)]
        [InlineData("BTC-WTC-ETH", 0.0383d, 0.0300d)]
        public void TestArbitrages(String arbitrageName, Double arbitrageProfit, Double quantity)
        {
            Utilities.LoadConfig();

            Trade trade = Utilities.DeserializeArbitrageFile<Trade>(arbitrageName, "Trade.json");
            (Decimal Price, Decimal Quantity)[][] asks = Utilities.DeserializeArbitrageFile<(Decimal Price, Decimal Quantity)[][]>(arbitrageName, "Asks.json");
            (Decimal Price, Decimal Quantity)[][] bids = Utilities.DeserializeArbitrageFile<(Decimal Price, Decimal Quantity)[][]>(arbitrageName, "Bids.json");
            
            SortedOrderBook[] orderBooks = new SortedOrderBook[3];

            for (Int32 i = 0; i < orderBooks.Length; ++i)
                orderBooks[i] = new SortedOrderBook(trade[i].Symbol, asks[i], bids[i]);

            MethodInfo method = typeof(TradingEngine).GetMethod("DetectArbitrage", BindingFlags.NonPublic | BindingFlags.Static);
            Object[] parameters = { trade, Utilities.ToDecimal(quantity, 4), orderBooks };

            Arbitrage arbitrage = (Arbitrage)method?.Invoke(null, parameters);

            Decimal? expected = Utilities.ToDecimal(arbitrageProfit, 4);
            Decimal? actual = (arbitrage == null) ? default(Decimal?) : Math.Round(arbitrage.Profit, 4);

            Assert.Equal(expected, actual);
        }
    }
}
