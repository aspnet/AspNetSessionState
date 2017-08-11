// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionStateCosmosDBSessionStateProviderAsync
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System;

    class PartitionKeyConverter : JsonConverter
    {
        public static string PartitionKey { get; set; }

        public override bool CanConvert(Type objectType)
        {
            return typeof(string) == objectType;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return string.Empty;
            }

            return reader.Value;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if(!string.IsNullOrEmpty(PartitionKey) && value != null)
            {
                var jo = new JObject();
                jo.Add(PartitionKey, new JValue(value));
                jo.WriteTo(writer);
            }
        }
    }
}
