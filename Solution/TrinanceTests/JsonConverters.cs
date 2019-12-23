#region Using Directives
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endregion

namespace TrinanceTests
{
    public class ValueTupleConverter<T1,T2> : JsonConverter
    {
        #region Methods
        public override Boolean CanConvert(Type objectType)
        {
            return (objectType == typeof(ValueTuple<T1,T2>));
        }

        public override Object ReadJson(JsonReader reader, Type objectType, Object existingValue, JsonSerializer serializer)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            if (reader.TokenType == JsonToken.Null)
                return null;

            JObject obj = JObject.Load(reader);
            List<JProperty> properties = obj.Properties().ToList();

            return (new ValueTuple<T1,T2>(obj[properties[0].Name].ToObject<T1>(), obj[properties[1].Name].ToObject<T2>()));
        }

        public override void WriteJson(JsonWriter writer, Object value, JsonSerializer serializer)
        {
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));

            serializer.Serialize(writer, value);
        }
        #endregion
    }
}