#region Using Directives
using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using TrinanceLib;
using TrinanceTests.Properties;
#endregion

namespace TrinanceTests
{
    internal static class Utilities
    {
        #region Methods
        public static Decimal ToDecimal(Double value, Int32 digits)
        {
            return Math.Round(Convert.ToDecimal(value), digits, MidpointRounding.AwayFromZero);
        }

        public static T DeserializeArbitrageFile<T>(String arbitrageName, String fileName)
        {
            if (String.IsNullOrWhiteSpace(arbitrageName))
                throw new ArgumentException(Resources.InvalidArbitrageName, nameof(arbitrageName));

            if (String.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException(Resources.InvalidArbitrageFileName, nameof(fileName));

            Uri codeBaseUrl = new Uri(Assembly.GetExecutingAssembly().CodeBase);
            String codeBasePath = Uri.UnescapeDataString(codeBaseUrl.AbsolutePath);
            
            String arbitrageDirectory = Path.Combine(Path.GetDirectoryName(codeBasePath), @"Data\Arbitrages", arbitrageName);
            String filePath = Path.Combine(arbitrageDirectory, fileName);

            if (!File.Exists(filePath))
                throw new FileNotFoundException(Resources.ArbitrageFileNotFound, filePath);

            JsonSerializerSettings settings = new JsonSerializerSettings { Converters = { new ValueTupleConverter<Decimal,Decimal>() } };

            return JsonConvert.DeserializeObject<T>(File.ReadAllText(filePath), settings);
        }

        public static void LoadConfig()
        {
            Uri codeBaseUrl = new Uri(Assembly.GetExecutingAssembly().CodeBase);
            String codeBasePath = Uri.UnescapeDataString(codeBaseUrl.AbsolutePath);
            String configPath = Path.Combine(Path.GetDirectoryName(codeBasePath), @"Data\Config.json");

            Config.Load(configPath);
        }
        #endregion
    }
}