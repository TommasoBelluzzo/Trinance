#region Using Directives
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrinanceLib.Properties;

#endregion

namespace TrinanceLib
{
    public sealed class TradingEngine : DisposableBase
    {
        #region Members
        private volatile Boolean m_Trading;
        private readonly BlockingCollection<Arbitrage> m_Arbitrages;
        private readonly Dictionary<DateTime,Execution> m_Executions;
        private readonly ExchangeEngine m_ExchangeEngine;
        private readonly HashSet<String> m_ExecutionSymbols;
        private readonly List<Int32> m_CycleDurations;
        private readonly MessagePump m_MessagePump;
        private readonly Task m_Consumer;
        private readonly Task m_Producer;
        #endregion

        #region Constructors
        public TradingEngine(MessagePump messagePump, ExchangeEngine exchangeEngine)
        {
            m_MessagePump = messagePump ?? throw new ArgumentNullException(nameof(messagePump));
            m_ExchangeEngine = exchangeEngine ?? throw new ArgumentNullException(nameof(exchangeEngine));

            if (!m_ExchangeEngine.Initialized)
                throw new Exception(Resources.EngineNotInitialized);

            m_Arbitrages = new BlockingCollection<Arbitrage>(new ConcurrentQueue<Arbitrage>());
            m_Executions = new Dictionary<DateTime,Execution>();
            m_ExecutionSymbols = new HashSet<String>();
            m_CycleDurations = new List<Int32>();

            String session = Utilities.FormatMessage(Resources.TradingSession, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            String sessionFrame = new String('*', session.Length);

            m_MessagePump.Signal(sessionFrame);
            m_MessagePump.Signal(session);
            m_MessagePump.Signal(sessionFrame);

            m_Consumer = Task.Factory.StartNew(() =>
            {
                foreach (Arbitrage arbitrage in m_Arbitrages.GetConsumingEnumerable())
                    Task.Run(() => ExecuteArbitrage(arbitrage));
            },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            m_Producer = Task.Factory.StartNew(() =>
            {
                Thread.Sleep(Config.TradingCyclesDelay * 10);

                while (!m_Arbitrages.IsCompleted)
                {
                    Task.Run(() => DetectArbitrages());
                    Thread.Sleep(Config.TradingCyclesDelay);
                }
            },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        #endregion

        #region Methods
        private Execution ExecuteArbitrageParallel(Arbitrage arbitrage)
        {
            Execution execution = new Execution(arbitrage);

            Parallel.ForEach(execution.GetSequence(), sequence =>
            {
                Int32 offset = sequence.Item1;
                TriangulationStep x = sequence.Item2;
                TriangulationStep y = sequence.Item3;

                if (!Config.TradingEnabled)
                {
                    m_ExchangeEngine.TestOrder(x.Position, x.Symbol, arbitrage.GetQuantity(offset));
                    return;
                }

                OrderResult result = m_ExchangeEngine.PlaceOrder(x.Position, x.Symbol, arbitrage.GetQuantity(offset));

                if (x.Position == Position.Buy)
                {
                    x.Out = result.OtherQuantity;
                    y.In = result.ExecutedQuantity;
                }
                else
                {
                    x.Out = result.ExecutedQuantity;
                    y.In = result.OtherQuantity;
                }

                execution.Fees += result.Fees;
            });

            return execution;
        }

        private Execution ExecuteArbitrageSequential(Arbitrage arbitrage)
        {
            Decimal? recalculatedQuantity = null;
            Execution execution = new Execution(arbitrage);

            foreach ((Int32 offset, TriangulationStep x, TriangulationStep y) in execution.GetSequence())
            {
                Decimal quantity = recalculatedQuantity ?? arbitrage.GetQuantity(offset);

                if (!Config.TradingEnabled)
                {
                    m_ExchangeEngine.TestOrder(x.Position, x.Symbol, quantity);
                    continue;
                }

                OrderResult result = m_ExchangeEngine.PlaceOrder(x.Position, x.Symbol, quantity);

                if (x.Position == Position.Buy)
                {
                    x.Out = result.OtherQuantity;
                    y.In = result.ExecutedQuantity;

                    SortedOrderBook orderBook = m_ExchangeEngine.GetOrderBook(y.Symbol);
                    recalculatedQuantity = CalculateDustless(OrderBookConversion(false, y.In, y.QuoteAsset, y.BaseAsset, orderBook), y.DustDecimals);
                }
                else
                {
                    x.Out = result.ExecutedQuantity;
                    y.In = result.OtherQuantity;
                    
                    recalculatedQuantity = CalculateDustless(y.In, y.DustDecimals);
                }

                execution.Fees += result.Fees;
            }

            return execution;
        }

        private void ExecuteArbitrage(Arbitrage arbitrage)
        {
            MessageCollection message = new MessageCollection(true, Utilities.FormatMessage(Resources.ArbitrageFound, arbitrage.Identifier, $"{arbitrage.Profit:F4}"));

            if (Config.TradingEnabled && (Config.TradingExecutionsCap > 0) && (m_Executions.Count >= Config.TradingExecutionsCap))
            {
                message.AppendMessage(Utilities.FormatMessage(Resources.ArbitrageDiscardedCap, Config.TradingExecutionsCap));
                m_MessagePump.Signal(message);
                return;
            }

            if (arbitrage.Profit < Config.TradingThresholdProfit)
            {
                message.AppendMessage(Utilities.FormatMessage(Resources.ArbitrageDiscardedProfit, $"{Config.TradingThresholdProfit:F4}"));
                return;
            }

            if (m_ExecutionSymbols.Any(arbitrage.GetReferenceAssets().Contains))
            {
                message.AppendMessage(Resources.ArbitrageDiscardedAssets);
                m_MessagePump.Signal(message);
                return;
            }

            if (m_Executions.Count(x => x.Key >= DateTime.Now.AddSeconds(-1d)) > 1)
            {
                message.AppendMessage(Resources.ArbitrageDiscardedCooldown);
                m_MessagePump.Signal(message);
                return;
            }

            Int32 age = (DateTime.Now - arbitrage.ReferenceTime).Milliseconds;

            if (age > Config.TradingThresholdAge)
            {
                message.AppendMessage(Utilities.FormatMessage(Resources.ArbitrageDiscardedAge, age, Config.TradingThresholdAge));
                m_MessagePump.Signal(message);
                return;
            }

            foreach (String referenceAsset in arbitrage.GetReferenceAssets())
                m_ExecutionSymbols.Add(referenceAsset);

            try
            {
                Execution execution;

                if (Config.TradingStrategy == Strategy.Parallel)
                    execution = ExecuteArbitrageParallel(arbitrage);
                else
                    execution = ExecuteArbitrageSequential(arbitrage);

                m_Executions[DateTime.Now] = execution;

                message.AppendMessage(Utilities.FormatMessage(Resources.ArbitrageSucceded, Config.TradingStrategy.ToString().ToUpperInvariant()));

                foreach (var zip in arbitrage.GetSequence().Zip(execution.GetSequence(), (e, o) => new { Expected = e, Observed = o }))
                {
                    Int32 offset = zip.Expected.Item1;

                    TriangulationStep eX = zip.Expected.Item2;
                    TriangulationStep eY = zip.Expected.Item3;

                    TriangulationStep oX = zip.Observed.Item2;
                    TriangulationStep oY = zip.Observed.Item3;

                    message.AppendMessage(Utilities.FormatMessage(Resources.ArbitrageStep, offset));
                    message.AppendMessage(Utilities.FormatMessage(Resources.ArbitrageExpectedConversion, $"{eX.Out:F8}", eX.ReferenceAsset, $"{eY.Out:F8}", eY.ReferenceAsset));
                    message.AppendMessage(Utilities.FormatMessage(Resources.ArbitrageObservedConversion, $"{oX.Out:F8}", oX.ReferenceAsset, $"{oY.Out:F8}", oY.ReferenceAsset));
                }

                message.AppendMessage(Resources.ArbitrageDeltas);

                foreach (TriangulationStep step in execution.GetSteps())
                {
                    Decimal eDelta = step.Delta;
                    Decimal ePercent = (eDelta / step.Out) * 100M;

                    message.AppendMessage(Utilities.FormatMessage(Resources.ArbitrageDelta, step.ReferenceAsset, (eDelta < 0M) ? String.Empty : " ", $"{eDelta:F8}", $"{ePercent:F4}"));
                }

                message.AppendMessage(Resources.ArbitrageOthers);
                message.AppendMessage(Utilities.FormatMessage(Resources.ArbitrageCommission, $"{(-1M * execution.Fees):F8}"));
            }
            catch (Exception e)
            {
                message.AppendMessage(Utilities.FormatMessage(Resources.ArbitrageFailed, Config.TradingStrategy.ToString().ToUpperInvariant(), Utilities.GetExceptionMessage(e)));
            }

            foreach (String referenceAsset in arbitrage.GetReferenceAssets())
                m_ExecutionSymbols.Remove(referenceAsset);

            m_MessagePump.Signal(message);
        }

        private void DetectArbitrages()
        {
            if (m_Trading)
            {
                m_MessagePump.Signal(Utilities.FormatMessage(Resources.CycleSkipped, Config.TradingCyclesDelay));
                return;
            }

            m_Trading = true;

            DateTime start = DateTime.Now;
            ICollection<Trade> trades = m_ExchangeEngine.GetTrades();
            IDictionary<String,SortedOrderBook> orderBooks = m_ExchangeEngine.GetOrderBooks();

            Parallel.ForEach(trades, trade =>
            {
                SortedOrderBook orderBook1 = orderBooks[trade.Leg1.Symbol];

                if (orderBook1.Invalid)
                    return;

                SortedOrderBook orderBook2 = orderBooks[trade.Leg2.Symbol];

                if (orderBook2.Invalid)
                    return;

                SortedOrderBook orderBook3 = orderBooks[trade.Leg3.Symbol];

                if (orderBook3.Invalid)
                    return;

                Arbitrage bestArbitrage = null;
                SortedOrderBook[] orderBooksArray = { orderBook1, orderBook2, orderBook3 };

                for (Decimal quantity = Config.InvestmentMinimum; quantity <= Config.InvestmentMaximum; quantity += Config.InvestmentStep)
                {
                    Arbitrage arbitrage = DetectArbitrage(trade, quantity, orderBooksArray);

                    if ((arbitrage == null) || (arbitrage.Profit <= 0M))
                        continue;

                    if ((bestArbitrage == null) || (arbitrage.Profit > bestArbitrage.Profit))
                        bestArbitrage = arbitrage;
                }

                if (bestArbitrage != null)
                    m_Arbitrages.Add(bestArbitrage);
            });

            Int32 duration = (DateTime.Now - start).Milliseconds;
            
            if (m_CycleDurations.Count == 300)
                m_CycleDurations.RemoveAt(0);

            m_CycleDurations.Add(duration);

            m_MessagePump.Signal(Utilities.FormatMessage(Resources.CycleCompleted, duration, $"{(Int32)Math.Round(m_CycleDurations.Average(), 0)}"));

            m_Trading = false;
        }

        protected override void ReleaseManagedResources()
        {
            m_Arbitrages.CompleteAdding();

            Task.WaitAll(m_Producer, m_Consumer);
            m_Producer.Dispose();
            m_Consumer.Dispose();

            m_Arbitrages.Dispose();

            base.ReleaseManagedResources();
        }
        #endregion

        #region Methods (Static)
        private static Arbitrage DetectArbitrage(Trade trade, Decimal quantity, SortedOrderBook[] orderBooks)
        {
            try
            {
                DateTime referenceTime = orderBooks.Min(x => x.UpdateTime);
                Arbitrage arbitrage = new Arbitrage(referenceTime, trade);

                foreach ((Int32 offset, TriangulationStep x, TriangulationStep y) in arbitrage.GetSequence())
                {
                    if (x.Position == Position.Buy)
                    {
                        SortedOrderBook orderBook = orderBooks[offset];
                        Decimal dustedQuantity = OrderBookConversion(false, quantity, x.ReferenceAsset, y.ReferenceAsset, orderBook);

                        y.In = CalculateDustless(dustedQuantity, x.DustDecimals);
                        x.Out = OrderBookConversion(true, y.In, y.ReferenceAsset, x.ReferenceAsset, orderBook);
                    }
                    else
                    {
                        x.Out = CalculateDustless(quantity, x.DustDecimals);
                        y.In = OrderBookConversion(false, x.Out, x.ReferenceAsset, y.ReferenceAsset, orderBooks[offset]);
                    }

                    quantity = y.In;
                }

                return arbitrage;
            }
            catch
            {
                return null;
            }
        }

        private static Decimal CalculateDustless(Decimal quantity, Int32 dustDecimals)
        {
            if ((quantity - Math.Truncate(quantity)) == 0M)
                return quantity;

            String quantityString = quantity.ToString("F16", CultureInfo.InvariantCulture);
            Int32 dotIndex = quantityString.IndexOf(".", StringComparison.OrdinalIgnoreCase);

            return Decimal.Parse(quantityString.Substring(0, dotIndex + dustDecimals + 1), CultureInfo.InvariantCulture);
        }

        private static Decimal OrderBookConversion(Boolean reversed, Decimal quantityFrom, String assetFrom, String assetTo, SortedOrderBook orderBook)
        {
            if (quantityFrom == 0M)
                return 0M;

            ReadOnlyCollection<SortedOrderBookElement> asks = orderBook.Asks;

            if (asks.Count == 0)
                throw new InvalidOperationException(Resources.NoAvailableAsks);

            ReadOnlyCollection<SortedOrderBookElement> bids = orderBook.Bids;

            if (bids.Count == 0)
                throw new Exception(Resources.NoAvailableBids);

            Decimal quantityTo = 0M;

            if (bids[0].Price > asks[0].Price)
                throw new Exception(Resources.NoBidAskSpread);

            if (reversed)
            {
                if ((assetFrom + assetTo) == orderBook.Symbol)
                {
                    foreach (SortedOrderBookElement ask in asks)
                    {
                        Decimal price = ask.Price;
                        Decimal quantity = ask.Quantity;
                        Decimal quantityExchangeable = price * quantity;

                        if (quantity >= quantityFrom)
                            return (quantityTo + (quantityFrom * price));

                        quantityFrom -= quantity;
                        quantityTo += quantityExchangeable;
                    }
                }
                else
                {
                    foreach (SortedOrderBookElement bid in bids)
                    {
                        Decimal price = bid.Price;
                        Decimal quantity = bid.Quantity;
                        Decimal quantityExchangeable = price * quantity;

                        if (quantityExchangeable >= quantityFrom)
                            return (quantityTo + (quantityFrom / price));

                        quantityFrom -= quantityExchangeable;
                        quantityTo += quantity;
                    }
                }
            }
            else
            {
                if ((assetFrom + assetTo) == orderBook.Symbol)
                {
                    foreach (SortedOrderBookElement bid in bids)
                    {
                        Decimal price = bid.Price;
                        Decimal quantity = bid.Quantity;
                        Decimal quantityExchangeable = price * quantity;

                        if (quantity >= quantityFrom)
                            return (quantityTo + (quantityFrom * price));

                        quantityFrom -= quantity;
                        quantityTo += quantityExchangeable;
                    }
                }
                else
                {
                    foreach (SortedOrderBookElement ask in asks)
                    {
                        Decimal price = ask.Price;
                        Decimal quantity = ask.Quantity;
                        Decimal quantityExchangeable = price * quantity;

                        if (quantityExchangeable >= quantityFrom)
                            return (quantityTo + (quantityFrom / price));

                        quantityFrom -= quantityExchangeable;
                        quantityTo += quantity;
                    }
                }
            }

            throw new Exception(Resources.DepthsTooShallow);
        }
        #endregion
    }
}
