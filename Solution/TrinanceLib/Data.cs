#region Using Directives
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Binance;
using TrinanceLib.Properties;

#endregion

namespace TrinanceLib
{
    public sealed class Arbitrage : Triangulation
    {
        #region Members
        private readonly DateTime m_ReferenceTime;
        #endregion

        #region Properties
        public DateTime ReferenceTime => m_ReferenceTime;
        #endregion

        #region Constructors
        public Arbitrage(DateTime referenceTime, Trade trade) : base(trade)
        {
            m_ReferenceTime = referenceTime;
        }
        #endregion
    }

    public sealed class Execution : Triangulation
    {
        #region Members
        private Decimal m_Fees;
        #endregion

        #region Properties
        public Decimal Fees { get => m_Fees; set => m_Fees = value; }
        #endregion

        #region Constructors
        public Execution(Arbitrage arbitrage) : base(arbitrage) { }
        #endregion
    }

    public sealed class OrderResult
    {
        #region Members
        private readonly Decimal m_ExecutedQuantity;
        private readonly Decimal m_Fees;
        private readonly Decimal m_OtherQuantity;
        #endregion

        #region Properties
        public Decimal ExecutedQuantity => m_ExecutedQuantity;
        public Decimal Fees => m_Fees;
        public Decimal OtherQuantity => m_OtherQuantity;
        #endregion

        #region Constructors
        public OrderResult(Decimal executedQuantity, Decimal otherQuantity, Decimal fees)
        {
            m_ExecutedQuantity = executedQuantity;
            m_Fees = fees;
            m_OtherQuantity = otherQuantity;
        }
        #endregion
    }

    public sealed class SortedOrderBook
    {
        #region Members
        private Boolean m_Invalid;
        private readonly DateTime m_UpdateTime;
        private readonly Int64 m_UpdateId;
        private readonly ReadOnlyCollection<SortedOrderBookElement> m_Asks;
        private readonly ReadOnlyCollection<SortedOrderBookElement> m_Bids;
        private readonly String m_Symbol;
        #endregion

        #region Properties
        public Boolean Invalid { get => m_Invalid; set => m_Invalid = value; }
        public DateTime UpdateTime => m_UpdateTime;
        public Int64 UpdateId => m_UpdateId;
        public ReadOnlyCollection<SortedOrderBookElement> Asks => m_Asks;
        public ReadOnlyCollection<SortedOrderBookElement> Bids => m_Bids;
        public String Symbol => m_Symbol;
        #endregion

        #region Constructors
        public SortedOrderBook(String symbol, IEnumerable<(Decimal Price, Decimal Quantity)> asks, IEnumerable<(Decimal Price, Decimal Quantity)> bids)
        {
            m_Symbol = symbol ?? throw new ArgumentException(Resources.InvalidSymbol, nameof(symbol));

            if (asks == null)
                throw new ArgumentNullException(nameof(asks));

            if (bids == null)
                throw new ArgumentNullException(nameof(bids));

            m_Invalid = false;
            m_UpdateTime = DateTime.Now;
            m_UpdateId = -1L;
            m_Asks = Sort(asks, true);
            m_Bids = Sort(bids, false);
        }

        public SortedOrderBook(OrderBook orderBook)
        {
            if (orderBook == null)
                throw new ArgumentNullException(nameof(orderBook));

            m_Invalid = false;
            m_UpdateTime = DateTime.Now;
            m_UpdateId = orderBook.LastUpdateId;
            m_Asks = Sort(orderBook.Asks, true);
            m_Bids = Sort(orderBook.Bids, false);
            m_Symbol = orderBook.Symbol;
        }
        #endregion

        #region Methods (Static)
        private static ReadOnlyCollection<SortedOrderBookElement> Sort(IEnumerable<(Decimal Price, Decimal Quantity)> elements, Boolean ascending)
        {
            List<SortedOrderBookElement> list;

            if (ascending)
                list = elements.OrderBy(x => x.Price).Take(Config.DataSize).Select(x => new SortedOrderBookElement(x.Price, x.Quantity)).ToList();
            else
                list = elements.OrderByDescending(x => x.Price).Take(Config.DataSize).Select(x => new SortedOrderBookElement(x.Price, x.Quantity)).ToList();

            return list.AsReadOnly();
        }

        private static ReadOnlyCollection<SortedOrderBookElement> Sort(IEnumerable<OrderBookPriceLevel> elements, Boolean ascending)
        {
            List<SortedOrderBookElement> list;

            if (ascending)
                list = elements.OrderBy(x => x.Price).Take(Config.DataSize).Select(x => new SortedOrderBookElement(x.Price, x.Quantity)).ToList();
            else
                list = elements.OrderByDescending(x => x.Price).Take(Config.DataSize).Select(x => new SortedOrderBookElement(x.Price, x.Quantity)).ToList();

            return list.AsReadOnly();
        }
        #endregion
    }

    public sealed class SortedOrderBookElement
    {
        #region Members
        private readonly Decimal m_Price;
        private readonly Decimal m_Quantity;
        #endregion

        #region Properties
        public Decimal Price => m_Price;
        public Decimal Quantity => m_Quantity;
        #endregion

        #region Constructors
        public SortedOrderBookElement(Decimal price, Decimal quantity)
        {
            m_Price = price;
            m_Quantity = quantity;
        }
        #endregion
    }

    public sealed class Trade
    {
        #region Members
        private readonly String m_Identifier;
        private readonly TradeLeg m_Leg1;
        private readonly TradeLeg m_Leg2;
        private readonly TradeLeg m_Leg3;
        #endregion

        #region Properties
        public String Identifier => m_Identifier;

        public TradeLeg this[Int32 leg]
        {
            get
            {
                switch (leg)
                {
                    case 0:
                        return m_Leg1;

                    case 1:
                        return m_Leg2;

                    case 2:
                        return m_Leg3;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(leg), leg, Resources.InvalidLeg);
                }
            }
        }

        public TradeLeg Leg1 => m_Leg1;
        public TradeLeg Leg2 => m_Leg2;
        public TradeLeg Leg3 => m_Leg3;
        #endregion

        #region Constructors
        public Trade(TradeLeg leg1, TradeLeg leg2, TradeLeg leg3)
        {
            m_Leg1 = leg1 ?? throw new ArgumentNullException(nameof(leg1));
            m_Leg2 = leg2 ?? throw new ArgumentNullException(nameof(leg2));
            m_Leg3 = leg3 ?? throw new ArgumentNullException(nameof(leg3));
            m_Identifier = $"{leg1.ReferenceAsset}-{leg2.ReferenceAsset}-{leg3.ReferenceAsset}";
        }
        #endregion
    }

    public sealed class TradeLeg
    {
        #region Members
        private readonly Int32 m_DustDecimals;
        private readonly Position m_Position;
        private readonly String m_BaseAsset;
        private readonly String m_QuoteAsset;
        private readonly String m_ReferenceAsset;
        private readonly String m_Symbol;
        #endregion

        #region Properties
        public Int32 DustDecimals => m_DustDecimals;
        public Position Position => m_Position;
        public String BaseAsset => m_BaseAsset;
        public String QuoteAsset => m_QuoteAsset;
        public String ReferenceAsset => m_ReferenceAsset;
        public String Symbol => m_Symbol;
        #endregion

        #region Constructors
        public TradeLeg(String symbol, String referenceAsset, String baseAsset, String quoteAsset, Position position, Int32 dustDecimals)
        {
            if (symbol == null)
                throw new ArgumentNullException(nameof(symbol));

            if (String.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException(Resources.InvalidSymbol, nameof(symbol));

            if (referenceAsset == null)
                throw new ArgumentNullException(nameof(referenceAsset));

            if (String.IsNullOrWhiteSpace(referenceAsset))
                throw new ArgumentException(Resources.InvalidReferenceAsset, nameof(referenceAsset));

            if (baseAsset == null)
                throw new ArgumentNullException(nameof(baseAsset));

            if (String.IsNullOrWhiteSpace(baseAsset))
                throw new ArgumentException(Resources.InvalidBaseAsset, nameof(baseAsset));

            if (quoteAsset == null)
                throw new ArgumentNullException(nameof(quoteAsset));

            if (String.IsNullOrWhiteSpace(quoteAsset))
                throw new ArgumentException(Resources.InvalidQuoteAsset, nameof(quoteAsset));

            if (!Enum.IsDefined(typeof(Position), position))
                throw new InvalidEnumArgumentException(Resources.InvalidPosition);

            if (dustDecimals < 0)
                throw new ArgumentException(Resources.InvalidDustDecimals, nameof(dustDecimals));

            m_DustDecimals = dustDecimals;
            m_Position = position;
            m_BaseAsset = baseAsset;
            m_QuoteAsset = quoteAsset;
            m_ReferenceAsset = referenceAsset;
            m_Symbol = symbol;
        }
        #endregion
    }

    public abstract class Triangulation
    {
        #region Members
        private readonly String m_Identifier;
        private readonly TriangulationStep m_A;
        private readonly TriangulationStep m_B;
        private readonly TriangulationStep m_C;
        #endregion

        #region Properties
        public Decimal Profit
        {
            get
            {
                if (m_A.In == 0M)
                    return 0M;

                return (((m_A.Delta / m_A.Out) * 100M) - (Config.TradingTakerFee * 3M));
            }
        }

        public Decimal Quantity1 => (m_A.Position == Position.Buy) ? m_B.In : m_A.Out;
        public Decimal Quantity2 => (m_A.Position == Position.Buy) ? m_C.In : m_B.Out;
        public Decimal Quantity3 => (m_A.Position == Position.Buy) ? m_A.In : m_C.Out;
        public String Identifier => m_Identifier;

        public TriangulationStep this[Int32 step]
        {
            get
            {
                switch (step)
                {
                    case 0:
                        return m_A;

                    case 1:
                        return m_B;

                    case 2:
                        return m_C;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(step), step, Resources.InvalidStep);
                }
            }
        }

        public TriangulationStep A => m_A;
        public TriangulationStep B => m_B;
        public TriangulationStep C => m_C;
        #endregion

        #region Constructors
        protected Triangulation(Trade trade)
        {
            if (trade == null)
                throw new ArgumentNullException(nameof(trade));

            m_Identifier = trade.Identifier;
            m_A = new TriangulationStep(trade.Leg1);
            m_B = new TriangulationStep(trade.Leg2);
            m_C = new TriangulationStep(trade.Leg3);
        }

        protected Triangulation(Triangulation other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            m_Identifier = other.Identifier;
            m_A = new TriangulationStep(other.A);
            m_B = new TriangulationStep(other.B);
            m_C = new TriangulationStep(other.C);
        }
        #endregion

        #region Methods
        public Decimal GetQuantity(Int32 step)
        {
            switch (step)
            {
                case 0:
                    return Quantity1;

                case 1:
                    return Quantity2;

                case 2:
                    return Quantity3;

                default:
                    throw new ArgumentOutOfRangeException(nameof(step), step, Resources.InvalidStep);
            }
        }

        public IEnumerable<String> GetReferenceAssets()
        {
            yield return m_A.ReferenceAsset;
            yield return m_B.ReferenceAsset;
            yield return m_C.ReferenceAsset;
        }

        public IEnumerable<(Int32, TriangulationStep, TriangulationStep)> GetSequence()
        {
            yield return (0, m_A, m_B);
            yield return (1, m_B, m_C);
            yield return (2, m_C, m_A);
        }

        public IEnumerable<TriangulationStep> GetSteps()
        {
            yield return m_A;
            yield return m_B;
            yield return m_C;
        }
        #endregion
    }

    public sealed class TriangulationStep
    {
        #region Members
        private Decimal m_In;
        private Decimal m_Out;
        private readonly Int32 m_DustDecimals;
        private readonly Position m_Position;
        private readonly String m_BaseAsset;
        private readonly String m_QuoteAsset;
        private readonly String m_ReferenceAsset;
        private readonly String m_Symbol;
        #endregion

        #region Properties
        public Decimal Delta => m_In - m_Out;
        public Decimal In { get => m_In; set => m_In = value; }
        public Decimal Out { get => m_Out; set => m_Out = value; }
        public Int32 DustDecimals => m_DustDecimals;
        public Position Position => m_Position;
        public String BaseAsset => m_BaseAsset;
        public String QuoteAsset => m_QuoteAsset;
        public String ReferenceAsset => m_ReferenceAsset;
        public String Symbol => m_Symbol;
        #endregion

        #region Constructors
        public TriangulationStep(TradeLeg leg)
        {
            if (leg == null)
                throw new ArgumentNullException(nameof(leg));

            m_DustDecimals = leg.DustDecimals;
            m_Position = leg.Position;
            m_BaseAsset = leg.BaseAsset;
            m_QuoteAsset = leg.QuoteAsset;
            m_ReferenceAsset = leg.ReferenceAsset;
            m_Symbol = leg.Symbol;
        }

        public TriangulationStep(TriangulationStep other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            m_DustDecimals = other.DustDecimals;
            m_Position = other.Position;
            m_BaseAsset = other.BaseAsset;
            m_QuoteAsset = other.QuoteAsset;
            m_ReferenceAsset = other.ReferenceAsset;
            m_Symbol = other.Symbol;
        }
        #endregion
    }
}
