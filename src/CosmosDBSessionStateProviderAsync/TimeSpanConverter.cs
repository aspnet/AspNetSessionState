// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using System;
    using Newtonsoft.Json;
    using Microsoft.AspNet.SessionState.Resources;

    class TimeSpanConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(TimeSpan) == objectType;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if(reader.TokenType == JsonToken.Null)
            {
                return new Nullable<TimeSpan>();
            }

            if (reader.TokenType != JsonToken.Integer)
            {
                throw new ArgumentException("reader");
            }

            return new TimeSpan(0, 0, Convert.ToInt32(reader.Value));
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if(value == null)
            {
                writer.WriteNull();
                return;
            }

            var ts = (TimeSpan)value;
            if(ts != null)
            {
                writer.WriteValue((int)ts.TotalSeconds);
            }
            else
            {
                throw new JsonSerializationException(string.Format(SR.Object_Cannot_Be_Converted_To_TimeSpan, "value"));
            }
        }
    }
}