// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Microsoft.AspNet.SessionState.Resources;

    class TimeSpanConverter : JsonConverter<TimeSpan?>
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeof(TimeSpan?) == typeToConvert;
        }

        public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return new Nullable<TimeSpan>();
            }

            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new ArgumentException("reader");
            }

            return new TimeSpan(0, 0, reader.GetInt32());
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            var ts = (TimeSpan)value;
            writer.WriteNumberValue((int)ts.TotalSeconds);
        }
    }
}