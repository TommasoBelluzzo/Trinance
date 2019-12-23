#region Using Directives
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using TrinanceLib.Properties;

#endregion

namespace TrinanceLib
{
    public static class Config
    {
        #region Constants
        private const Int32 DATA_STAGGER_LIMIT = 50;
        private const Int32 TRADING_CYCLES_DELAY_LIMIT = 50;
        private const Int32 TRADING_THRESHOLD_AGE_LIMIT = 100;
        #endregion

        #region Members
        private static readonly Regex s_RegexAsset = new Regex(@"^[A-Z]{3,6}$", RegexOptions.Compiled);
        private static readonly Regex s_RegexKey = new Regex(@"^[A-Za-z0-9]{10,}$", RegexOptions.Compiled);
        #endregion

        #region Properties
        public static Boolean TradingEnabled { get; private set; }
        public static Decimal InvestmentMaximum { get; private set; }
        public static Decimal InvestmentMinimum { get; private set; }
        public static Decimal InvestmentStep { get; private set; }
        public static Decimal TradingTakerFee { get; private set; }
        public static Decimal TradingThresholdProfit { get; private set; }
        public static Exchange Exchange { get; private set; }
        public static Int32 DataSize { get; private set; }
        public static Int32 DataStagger { get; private set; }
        public static Int32 TradingCyclesDelay { get; private set; }
        public static Int32 TradingExecutionsCap { get; private set; }
        public static Int32 TradingThresholdAge { get; private set; }
        public static ReadOnlyCollection<Position> TradingPositions { get; private set; }
        public static ReadOnlyCollection<String> TradingWhitelist { get; private set; }
        public static Strategy TradingStrategy { get; private set; }
        public static String InvestmentBaseAsset { get; private set; }
        public static String KeyApi { get; private set; }
        public static String KeySecret { get; private set; }
        #endregion

        #region Methods
        public static void Load(String configPath)
        {
            if (String.IsNullOrWhiteSpace(configPath))
                throw new ArgumentException(Resources.ConfigurationFileInvalid, nameof(configPath));

            if (!File.Exists(configPath))
                throw new FileNotFoundException(Resources.ConfigurationFileNotFound, configPath);

            String configData = File.ReadAllText(configPath);

            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                DefaultValueHandling = DefaultValueHandling.Populate,
                MissingMemberHandling = MissingMemberHandling.Error
            };

            ConfigBase c = JsonConvert.DeserializeObject<ConfigBase>(configData, settings);

            /* KEYS VALIDATION */

            if (String.IsNullOrWhiteSpace(c.Keys.Api) || !s_RegexKey.IsMatch(c.Keys.Api))
                throw new InvalidDataException(Resources.InvalidKeysApi);

            if (String.IsNullOrWhiteSpace(c.Keys.Secret) || !s_RegexKey.IsMatch(c.Keys.Secret))
                throw new InvalidDataException(Resources.InvalidKeysSecret);

            /* DATA VALIDATION */

            if (c.Data.Stagger < DATA_STAGGER_LIMIT)
                throw new InvalidDataException(Utilities.FormatMessage(Resources.InvalidDataStagger, DATA_STAGGER_LIMIT));
            
            /* INVESTMENT VALIDATION */

            if (String.IsNullOrWhiteSpace(c.Investment.BaseAsset) || !s_RegexAsset.IsMatch(c.Investment.BaseAsset))
                throw new InvalidDataException(Resources.InvalidInvestmentBaseAsset);

            if (c.Investment.Minimum <= 0M)
                throw new InvalidDataException(Resources.InvalidInvestmentMinimum);

            if (c.Investment.Maximum <= 0M)
                throw new InvalidDataException(Resources.InvalidInvestmentMaximum);

            if (c.Investment.Step <= 0M)
                throw new InvalidDataException(Resources.InvalidInvestmentStep);

            if (c.Investment.Minimum > c.Investment.Maximum)
                throw new InvalidDataException(Resources.InvalidInvestmentMinimumMaximum);

            if ((c.Investment.Minimum != c.Investment.Maximum) && (((c.Investment.Maximum - c.Investment.Minimum) / c.Investment.Step) < 1M))
                throw new InvalidDataException(Resources.InvalidInvestmentSteps);

            /* TRADING VALIDATION */

            if (c.Trading.CyclesDelay < TRADING_CYCLES_DELAY_LIMIT)
                throw new InvalidDataException(Utilities.FormatMessage(Resources.InvalidTradingCyclesDelay, TRADING_CYCLES_DELAY_LIMIT));

            if (c.Trading.ExecutionsCap < 0)
                throw new InvalidDataException(Resources.InvalidTradingExecutionsCap);

            if ((c.Trading.Positions.Count != 3) || (c.Trading.Positions.Distinct().Count() != 2))
                throw new InvalidDataException(Resources.InvalidTradingPositions);

            if ((c.Trading.Strategy == Strategy.Parallel) && (c.Trading.Whitelist.Count == 0))
                throw new InvalidDataException(Resources.InvalidTradingStrategyParallel);

            if (c.Trading.TakerFee < 0M)
                throw new InvalidDataException(Resources.InvalidTradingTakerFee);

            if (c.Trading.ThresholdAge < TRADING_THRESHOLD_AGE_LIMIT)
                throw new InvalidDataException(Utilities.FormatMessage(Resources.InvalidTradingThresholdAge, TRADING_THRESHOLD_AGE_LIMIT));

            if (c.Trading.ThresholdProfit <= 0M)
                throw new InvalidDataException(Resources.InvalidTradingThresholdProfit);

            if (c.Trading.Whitelist.Count > 0)
            {
                if (c.Trading.Whitelist.Any(x => !s_RegexAsset.IsMatch(x)))
                    throw new InvalidDataException(Resources.InvalidTradingWhitelistAssets);

                if (!c.Trading.Whitelist.Contains(c.Investment.BaseAsset))
                    throw new InvalidDataException(Resources.InvalidTradingWhitelistBaseAsset);
            }

            /* FINALIZATION */

            Exchange = c.BaseExchange;

            KeyApi = c.Keys.Api;
            KeySecret = c.Keys.Secret;

            DataSize = (Int32)c.Data.Size;
            DataStagger = c.Data.Stagger;

            InvestmentBaseAsset = c.Investment.BaseAsset;
            InvestmentMinimum = c.Investment.Minimum;
            InvestmentMaximum = c.Investment.Maximum;
            InvestmentStep = c.Investment.Step;

            TradingEnabled = c.Trading.Enabled;
            TradingCyclesDelay = c.Trading.CyclesDelay;
            TradingExecutionsCap = c.Trading.ExecutionsCap;
            TradingPositions = (c.Trading.Positions ?? new List<Position> { Position.Buy, Position.Sell, Position.Sell }).AsReadOnly();
            TradingStrategy = c.Trading.Strategy;
            TradingTakerFee = c.Trading.TakerFee;
            TradingThresholdAge = c.Trading.ThresholdAge;
            TradingThresholdProfit = c.Trading.ThresholdProfit;
            TradingWhitelist = (c.Trading.Whitelist ?? new List<String>()).AsReadOnly();
        }
        #endregion

        #region Nested Classes
        private sealed class ConfigBase
        {
            #region Members
            #pragma warning disable 0649
            [JsonProperty("KEYS")]
            private readonly ConfigKeys m_Keys;

            [JsonProperty("DATA")]
            private readonly ConfigData m_Data;

            [JsonProperty("INVESTMENT")]
            private readonly ConfigInvestment m_Investment;

            [JsonProperty("TRADING")]
            private readonly ConfigTrading m_Trading;

            [JsonProperty("EXCHANGE"), DefaultValue(Exchange.Binance)]
            private readonly Exchange m_Exchange;
            #pragma warning restore 0649
            #endregion

            #region Properties
            public ConfigKeys Keys => m_Keys;
            public ConfigData Data => m_Data;
            public ConfigInvestment Investment => m_Investment;
            public ConfigTrading Trading => m_Trading;
            public Exchange BaseExchange => m_Exchange;
            #endregion
        }

        private sealed class ConfigKeys
        {
            #region Members
            #pragma warning disable 0649
            [JsonProperty("API", Required = Required.Always), DefaultValue(null)]
            private readonly String m_Api;

            [JsonProperty("SECRET", Required = Required.Always), DefaultValue(null)]
            private readonly String m_Secret;
            #pragma warning restore 0649
            #endregion

            #region Properties
            public String Api => m_Api;
            public String Secret => m_Secret;
            #endregion
        }

        private sealed class ConfigData
        {
            #region Members
            #pragma warning disable 0649
            [JsonProperty("SIZE"), DefaultValue(DepthSize.DS20)]
            private readonly DepthSize m_Size;

            [JsonProperty("STAGGER"), DefaultValue(50u)]
            private readonly Int32 m_Stagger;
            #pragma warning restore 0649
            #endregion

            #region Properties
            public DepthSize Size => m_Size;
            public Int32 Stagger => m_Stagger;
            #endregion
        }

        private sealed class ConfigInvestment
        {
            #region Members
            #pragma warning disable 0649
            [JsonProperty("MAXIMUM"), DefaultValue(0.035)]
            private readonly Decimal m_Maximum;

            [JsonProperty("MINIMUM"), DefaultValue(0.015)]
            private readonly Decimal m_Minimum;

            [JsonProperty("STEP"), DefaultValue(0.005)]
            private readonly Decimal m_Step;

            [JsonProperty("BASE_ASSET"), DefaultValue("BTC")]
            private readonly String m_BaseAsset;
            #pragma warning restore 0649
            #endregion

            #region Properties
            public Decimal Maximum => m_Maximum;
            public Decimal Minimum => m_Minimum;
            public Decimal Step => m_Step;
            public String BaseAsset => m_BaseAsset;
            #endregion
        }

        private sealed class ConfigTrading
        {
            #region Members
            #pragma warning disable 0649
            [JsonProperty("ENABLED"), DefaultValue(false)]
            private readonly Boolean m_Enabled;

            [JsonProperty("TAKER_FEE"), DefaultValue(0)]
            private readonly Decimal m_TakerFee;

            [JsonProperty("THRESHOLD_PROFIT"), DefaultValue(0.3)]
            private readonly Decimal m_ThresholdProfit;

            [JsonProperty("CYCLES_DELAY"), DefaultValue(250)]
            private readonly Int32 m_CyclesDelay;

            [JsonProperty("EXECUTIONS_CAP"), DefaultValue(1)]
            private readonly Int32 m_ExecutionsCap;

            [JsonProperty("THRESHOLD_AGE"), DefaultValue(300)]
            private readonly Int32 m_ThresholdAge;

            [JsonProperty("POSITIONS"), DefaultValue(null)]
            private readonly List<Position> m_Positions;

            [JsonProperty("WHITELIST"), DefaultValue(null)]
            private readonly List<String> m_Whitelist;

            [JsonProperty("STRATEGY"), DefaultValue(Strategy.Sequential)]
            private readonly Strategy m_Strategy;
            #pragma warning restore 0649
            #endregion

            #region Properties
            public Boolean Enabled => m_Enabled;
            public Decimal TakerFee => m_TakerFee;
            public Decimal ThresholdProfit => m_ThresholdProfit;
            public Int32 CyclesDelay => m_CyclesDelay;
            public Int32 ExecutionsCap => m_ExecutionsCap;
            public Int32 ThresholdAge => m_ThresholdAge;
            public List<Position> Positions => m_Positions;
            public List<String> Whitelist => m_Whitelist;
            public Strategy Strategy => m_Strategy;
            #endregion
        }
        #endregion
    }
}