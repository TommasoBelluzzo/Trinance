#region Using Directives
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Binance;
using Binance.Cache;
using Binance.Client;
using Binance.WebSocket;
using TrinanceLib.Properties;
#endregion

namespace TrinanceLib
{
    public abstract class ExchangeEngine : DisposableBase
    {
        #region Members
        protected Boolean m_Initialized;
        protected readonly ConcurrentDictionary<String,Decimal> m_Balances;
        protected readonly ConcurrentDictionary<String,SortedOrderBook> m_OrderBooks;
        protected readonly List<Trade> m_Trades;
        protected readonly MessagePump m_MessagePump;
        #endregion

        #region Properties
        public Boolean Initialized => m_Initialized;
        #endregion

        #region Constructors
        protected ExchangeEngine(MessagePump messagePump)
        {
            m_MessagePump = messagePump ?? throw new ArgumentNullException(nameof(messagePump));

            m_Balances = new ConcurrentDictionary<String,Decimal>();
            m_OrderBooks = new ConcurrentDictionary<String,SortedOrderBook>();
            m_Trades = new List<Trade>();
        }
        #endregion

        #region Methods
        public Decimal GetBalance(String asset)
        {
            m_Balances.TryGetValue(asset, out Decimal balance);
            return balance;
        }

        public ICollection<String> GetSymbols()
        {
            return (new List<String>(m_OrderBooks.Keys));
        }

        public  ICollection<Trade> GetTrades()
        {
            return (new List<Trade>(m_Trades));
        }

        public IDictionary<String,Decimal> GetBalances()
        {
            return (new Dictionary<String,Decimal>(m_Balances));
        }
        
        public SortedOrderBook GetOrderBook(String symbol)
        {
            m_OrderBooks.TryGetValue(symbol, out SortedOrderBook orderBook);
            return orderBook;
        }

        public IDictionary<String,SortedOrderBook> GetOrderBooks()
        {
            return (new Dictionary<String,SortedOrderBook>(m_OrderBooks));
        }

        public Trade GetTrade(String identifier)
        {
            return m_Trades.SingleOrDefault(x => x.Identifier == identifier);
        }
        #endregion

        #region Methods (Abstract)
        public abstract Boolean Initialize();
        public abstract OrderResult PlaceOrder(Position position, String symbol, Decimal quantity);
        public abstract void TestOrder(Position position, String symbol, Decimal quantity);
        #endregion

        #region Methods (Static)
        protected static TradeLeg BuildTradeLeg(Dictionary<String,Int32> symbolsData, String asset1, String asset2)
        {
            if (symbolsData == null)
                throw new ArgumentNullException(nameof(symbolsData));

            String symbol12 = asset1 + asset2;

            if (symbolsData.ContainsKey(symbol12))
                return (new TradeLeg(symbol12, asset1, asset1, asset2, Position.Sell, symbolsData[symbol12]));

            String symbol21 = asset2 + asset1;

            if (symbolsData.ContainsKey(symbol21))
                return (new TradeLeg(symbol21, asset1, asset2, asset1, Position.Buy, symbolsData[symbol21]));

            return null;
        }

        public static ExchangeEngine FromConfiguration(MessagePump messagePump)
        {
            switch (Config.Exchange)
            {
                case Exchange.Binance:
                    return (new BinanceEngine(messagePump));

                default:
                    throw new InvalidOperationException(Resources.InvalidConfigurationExchange);
            }
        }
        #endregion
    }

    public sealed class BinanceEngine : ExchangeEngine
    {
        #region Members
        private readonly BinanceApi m_Api;
        private readonly Dictionary<String,DepthWebSocketCache> m_OrderBooksCache;
        private IBinanceApiUser m_User;
        private UserDataWebSocketManager m_UserDataWebSocket;
        #endregion
        
        #region Constructors
        public BinanceEngine(MessagePump messagePump) : base(messagePump)
        {
            m_Api = new BinanceApi();
            m_OrderBooksCache = new Dictionary<String,DepthWebSocketCache>();
        }
        #endregion

        #region Methods
        private void UpdateBalances(AccountInfo accountInfo)
        {
            m_Balances.Clear();

            foreach (AccountBalance balance in accountInfo.Balances)
                m_Balances[balance.Asset] = balance.Free;
        }

        protected override void ReleaseManagedResources()
        {
            if (m_OrderBooksCache != null)
            {
                try
                {
                    foreach (DepthWebSocketCache cache in m_OrderBooksCache.Values)
                        cache.Unsubscribe();
                }
                catch { }
            }

            if (m_UserDataWebSocket != null)
            {
                try
                {
                    m_UserDataWebSocket.UnsubscribeAllAsync().Wait();
                    m_UserDataWebSocket.Dispose();
                }
                catch { }
            }

            if (m_User != null)
            {
                try
                {
                    m_User.Dispose();
                }
                catch { }
            }

            base.ReleaseManagedResources();
        }

        public override Boolean Initialize()
        {
            m_MessagePump.Signal(Resources.ExchangeInitialization);

            try
            {
                BinanceStatus status = m_Api.GetSystemStatusAsync().Result;

                if (status == BinanceStatus.Maintenance)
                    throw new Exception(Resources.ServerMaintenance);

                TimeSpan delta = TimeSpan.Zero;

                for (Int32 i = 0; i < 5; ++i)
                {
                    DateTime now = DateTime.Now;
                    Boolean result = m_Api.PingAsync().Result;

                    if (!result)
                        throw new Exception(Resources.ServerPing);
                        
                    delta += DateTime.Now - now;
                }

                m_MessagePump.Signal(Utilities.FormatMessage(Resources.MessageConnectivityOK, delta.Milliseconds / 5));
            }
            catch (Exception e)
            {
                m_MessagePump.Signal(Utilities.FormatMessage(Resources.ConnectivityKO, Utilities.GetExceptionMessage(e)));
                return false;
            }

            try
            {
                m_User = new BinanceApiUser(Config.KeyApi, Config.KeySecret);
                m_MessagePump.Signal(Resources.LoginOK);
            }
            catch (Exception e)
            {
                m_MessagePump.Signal(Utilities.FormatMessage(Resources.LoginKO, Utilities.GetExceptionMessage(e)));
                return false;
            }

            try
            {
                UpdateBalances(m_Api.GetAccountInfoAsync(m_User).Result);

                m_UserDataWebSocket = new UserDataWebSocketManager();
                m_UserDataWebSocket.SubscribeAsync<AccountUpdateEventArgs>(m_User, x => UpdateBalances(x.AccountInfo)).Wait();
                m_UserDataWebSocket.WaitUntilWebSocketOpenAsync().Wait();

                if (m_Balances.Count == 0)
                    m_MessagePump.Signal(Resources.BalancesOK0);
                else
                {
                    m_MessagePump.Signal(Utilities.FormatMessage(Resources.BalancesKO, m_Balances.Count, (m_Balances.Count == 1 ? String.Empty : "s")));

                    foreach (KeyValuePair<String,Decimal> balance in m_Balances.OrderBy(x => x.Key))
                        m_MessagePump.Signal($" - {balance.Value:F8} {balance.Key}");
                }
            }
            catch (Exception e)
            {
                m_MessagePump.Signal(Utilities.FormatMessage(Resources.BalancesKO, Utilities.GetExceptionMessage(e)));
                return false;
            }

            try
            {
                Dictionary<String,Int32> symbolsData = new Dictionary<String,Int32>();
                HashSet<String> assets = new HashSet<String>();

                foreach (Symbol symbol in m_Api.GetSymbolsAsync().Result)
                {
                    if (symbol.Status != SymbolStatus.Trading)
                        continue;

                    String baseAsset = symbol.BaseAsset;
                    String quoteAsset = symbol.BaseAsset;
                    String name = symbol.ToString();

                    if ((Config.TradingWhitelist.Count > 0) && (!Config.TradingWhitelist.Contains(baseAsset) || !Config.TradingWhitelist.Contains(quoteAsset)))
                        continue;

                    String mininumQuantity = symbol.Quantity.Minimum.ToString(CultureInfo.InvariantCulture);
                    Int32 dustDecimals = Math.Max(0, mininumQuantity.IndexOf("1", StringComparison.OrdinalIgnoreCase) - 1);

                    assets.Add(baseAsset);
                    assets.Add(quoteAsset);
                    symbolsData[name] = dustDecimals;
                }

                ConcurrentBag<Trade> trades = new ConcurrentBag<Trade>();

                Parallel.ForEach(assets.SelectMany(x => assets, (a1, a2) => new { Asset1 = a1, Asset2 = a2 }), pair =>
                {
                    if (pair.Asset1 == pair.Asset2)
                        return;

                    TradeLeg leg1 = BuildTradeLeg(symbolsData, Config.InvestmentBaseAsset, pair.Asset1);

                    if ((leg1 == null) || (leg1.Position != Config.TradingPositions[0]))
                        return;

                    TradeLeg leg2 = BuildTradeLeg(symbolsData, pair.Asset1, pair.Asset2);

                    if ((leg2 == null) || (leg2.Position != Config.TradingPositions[1]))
                        return;

                    TradeLeg leg3 = BuildTradeLeg(symbolsData, pair.Asset2, Config.InvestmentBaseAsset);

                    if ((leg3 == null) || (leg3.Position != Config.TradingPositions[2]))
                        return;

                    trades.Add(new Trade(leg1, leg2, leg3));
                });

                if (trades.Count == 0)
                    throw new Exception(Resources.NoTriangularRelationships);

                m_Trades.AddRange(trades);

                m_MessagePump.Signal(Utilities.FormatMessage(Resources.TradesOK, m_Trades.Count, (m_Trades.Count == 1 ? String.Empty : "s")));
            }
            catch (Exception e)
            {
                m_MessagePump.Signal(Utilities.FormatMessage(Resources.TradesKO, Utilities.GetExceptionMessage(e)));
                return false;
            }

            try
            {
                List<String> symbols = m_Trades.Select(x => x.Leg1.Symbol)
                    .Union(m_Trades.Select(x => x.Leg2.Symbol))
                    .Union(m_Trades.Select(x => x.Leg3.Symbol))
                    .Distinct().OrderBy(x => x).ToList();

                foreach (String symbol in symbols)
                {
                    m_OrderBooks[symbol] = new SortedOrderBook(m_Api.GetOrderBookAsync(symbol, Config.DataSize).Result);
                    
                    m_OrderBooksCache[symbol] = new DepthWebSocketCache();
                    m_OrderBooksCache[symbol].Error += (sender, e) => m_OrderBooks[symbol].Invalid = true;
                    m_OrderBooksCache[symbol].OutOfSync += (sender, e) => m_OrderBooks[symbol].Invalid = true;
                    m_OrderBooksCache[symbol].Subscribe(symbol, e => m_OrderBooks[symbol] = new SortedOrderBook(e.OrderBook));

                    Thread.Sleep(Config.DataStagger);
                }

                m_MessagePump.Signal(Utilities.FormatMessage(Resources.OrderBooksOK, symbols.Count, (symbols.Count == 1 ? String.Empty : "s")));
                m_MessagePump.Signal(String.Empty);

                m_Initialized = true;

                return true;
            }
            catch (Exception e)
            {
                m_MessagePump.Signal(Utilities.FormatMessage(Resources.OrderBooksKO, Utilities.GetExceptionMessage(e)));
                return false;
            }
        }

        public override OrderResult PlaceOrder(Position position, String symbol, Decimal quantity)
        {
            MarketOrder order = new MarketOrder(m_User)
            {
                Quantity = quantity,
                Side = (position == Position.Buy) ? OrderSide.Buy : OrderSide.Sell,
                Symbol = symbol
            };

            Order result = m_Api.PlaceAsync(order).Result;
            Decimal executedQuantity = result.ExecutedQuantity;
            Decimal otherQuantity = result.CummulativeQuoteAssetQuantity;
            Decimal fees = result.Fills.Where(x => x.CommissionAsset == "BNB").Sum(x => x.Commission);

            return (new OrderResult(executedQuantity, otherQuantity, fees));
        }

        public override void TestOrder(Position position, String symbol, Decimal quantity)
        {
            MarketOrder order = new MarketOrder(m_User)
            {
                Quantity = quantity,
                Side = (position == Position.Buy) ? OrderSide.Buy : OrderSide.Sell,
                Symbol = symbol
            };

            m_Api.TestPlaceAsync(order).Wait();
        }
        #endregion
    }
}
