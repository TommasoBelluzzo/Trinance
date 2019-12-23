#region Using Directives
using System;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using TrinanceLib.Properties;

#endregion

namespace TrinanceLib
{
    [JsonConverter(typeof(ValidEnumConverter))]
    public enum DepthSize
    {
        #region Values
        [EnumMember(Value = "5")] DS5 = 5,
        [EnumMember(Value = "10")] DS10 = 10,
        [EnumMember(Value = "20")] DS20 = 20,
        [EnumMember(Value = "50")] DS50 = 50,
        [EnumMember(Value = "100")] DS100 = 100
        #endregion
    }
 
    [JsonConverter(typeof(ValidEnumConverter))]
    public enum Exchange
    {
        #region Values
        [EnumMember(Value = "BINANCE")] Binance
        #endregion
    }

    [JsonConverter(typeof(ValidEnumConverter))]
    public enum Position
    {
        #region Values
        [EnumMember(Value = "BUY")] Buy,
        [EnumMember(Value = "SELL")] Sell
        #endregion
    }

    [JsonConverter(typeof(ValidEnumConverter))]
    public enum Strategy
    {
        #region Values
        [EnumMember(Value = "PARALLEL")] Parallel,
        [EnumMember(Value = "SEQUENTIAL")] Sequential
        #endregion
    }

    public sealed class ValidEnumConverter : StringEnumConverter
    {
        #region Methods
        public override Object ReadJson(JsonReader reader, Type objectType, Object existingValue, JsonSerializer serializer)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            if (objectType == null)
                throw new ArgumentNullException(nameof(objectType));

            Boolean objectTypeNullable = objectType.IsGenericType && (objectType.GetGenericTypeDefinition() == typeof(Nullable<>));
            
            if (reader.TokenType == JsonToken.Null)
            {
                if (!objectTypeNullable)
                    throw new JsonSerializationException(Utilities.FormatMessage(Resources.UnhandledTokenNull, objectType));

                return null;
            }

            String readerValue = reader.Value?.ToString().Trim().ToUpperInvariant();
            Type enumType = (objectTypeNullable ? Nullable.GetUnderlyingType(objectType) : objectType) ?? objectType;

            switch (reader.TokenType)
            {
                case JsonToken.Integer:
                {
                    Type enumUnderlyingType = Enum.GetUnderlyingType(enumType);
                    String[] enumValues = Enum.GetValues(enumType).OfType<Object>().Select(x => Convert.ChangeType(x, enumUnderlyingType, CultureInfo.InvariantCulture).ToString()).ToArray();

                    if (!enumValues.Contains(readerValue))
                        throw new JsonSerializationException(Resources.InvalidEnumeratorValue);

                    return base.ReadJson(reader, objectType, existingValue, serializer);
                }

                case JsonToken.String:
                {
                    String[] enumNames = Enum.GetNames(enumType).Select(x => x.ToUpperInvariant()).ToArray();

                    if (!enumNames.Contains(readerValue))
                        throw new JsonSerializationException(Resources.InvalidEnumeratorValue);

                    return base.ReadJson(reader, objectType, existingValue, serializer);
                }

                default:
                    throw new JsonSerializationException(Utilities.FormatMessage(Resources.UnhandledTokenType, reader.TokenType, GetType().Name));
            }
        }
        #endregion
    }
}
