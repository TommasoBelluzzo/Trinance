#region Using Directives
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using TrinanceLib;
using TrinanceApp.Properties;
#endregion

namespace TrinanceApp
{
    public static class Program
    {
        #region Entry Point
        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        public static void Main()
        {
            /* INSTANCE */

            String assemblyGuid = Assembly.GetExecutingAssembly().GetCustomAttributes(false).OfType<GuidAttribute>().SingleOrDefault()?.Value ?? "Trinance";
            Mutex mutex = new Mutex(true, String.Format(CultureInfo.InvariantCulture, @"Global\{{{0}}}", assemblyGuid));

            if (!mutex.WaitOne(TimeSpan.FromSeconds(5), false))
            {
                Console.WriteLine(Resources.AnotherInstance);
                Exit(mutex, 0x01);
            }

            /* CONFIGURATION */

            Console.WriteLine(Resources.EnvironmentInitialization);

            try
            {
                Uri assemblyUri = new Uri(Assembly.GetExecutingAssembly().CodeBase);
                String baseDirectory = Path.GetDirectoryName(assemblyUri.LocalPath) ?? String.Empty;
                String configPath = Path.Combine(baseDirectory, "Config.json");

                Config.Load(configPath);

                Console.WriteLine(FormatMessage(Resources.BaseDirectory, NativeMethods.GetShortPath(baseDirectory)));
                Console.WriteLine(FormatMessage(Resources.Investment, Config.InvestmentBaseAsset, Config.InvestmentMinimum, Config.InvestmentMaximum, Config.InvestmentStep));
                Console.WriteLine(FormatMessage(Resources.TradingStatus, Config.TradingEnabled ? Resources.Enabled : Resources.Disabled, (!Config.TradingEnabled || (Config.TradingExecutionsCap == 0)) ? String.Empty : FormatMessage(Resources.TradingCap, Config.TradingExecutionsCap)));
                Console.WriteLine(FormatMessage(Resources.TradingProcess, Config.TradingStrategy, Config.TradingPositions[0], Config.TradingPositions[1], Config.TradingPositions[2]));
                Console.WriteLine();
            }
            catch (Exception e)
            {
                Console.WriteLine(FormatMessage(Resources.ConfigurationKO, GetExceptionMessage(e)));
                Exit(mutex, 0x02);
            }

            /* PROCESS */

            using (ConsolePump pump = new ConsolePump())
            using (ExchangeEngine exchangeEngine = ExchangeEngine.FromConfiguration(pump))
            {
                if (!exchangeEngine.Initialize())
                    Exit(mutex, 0x03);

                using (AutoResetEvent are = new AutoResetEvent(false))
                {
                    Console.CancelKeyPress += (o, e) => are.Set();

                    using (new TradingEngine(pump, exchangeEngine))
                        are.WaitOne();
                }
            }

            Exit(mutex, 0x00);
        }
        #endregion

        #region Methods
        private static String FormatMessage(String message, params Object[] parameters)
        {
            return String.Format(CultureInfo.InvariantCulture, message, parameters);
        }

        private static String GetExceptionMessage(Exception e)
        {
            String message = e?.Message;

            if (String.IsNullOrWhiteSpace(message))
                message = Resources.InvalidExceptionMessage;

            return (Char.ToLower(message[0], CultureInfo.InvariantCulture) + message.Substring(1).TrimEnd('.'));
        }

        private static void Exit(Mutex mutex, Int32 errorCode)
        {
            mutex.ReleaseMutex();
            mutex.Close();

            Environment.Exit(errorCode);
        }
        #endregion
    }
}
