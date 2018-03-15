// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionStateCosmosDBSessionStateProviderAsync {
    using Microsoft.AspNet.SessionStateCosmosDBSessionStateProviderAsync.Resources;
    using Newtonsoft.Json;
    using System;
    using System.Web.SessionState;

    class SessionStateActionsConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(SessionStateActions) == objectType;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if(reader.TokenType == JsonToken.Null)
            {
                return new Nullable<SessionStateActions>();
            }

            if (reader.TokenType != JsonToken.Boolean)
            {
                throw new ArgumentException("reader");
            }

            return  Convert.ToBoolean(reader.Value) ? SessionStateActions.InitializeItem : SessionStateActions.None;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if(value == null)
            {
                writer.WriteNull();
                return;
            }

            SessionStateActions action;
            if(Enum.TryParse<SessionStateActions>(value.ToString(), out action))
            {
                var valToWrite = action == SessionStateActions.None ? true : false;
                writer.WriteValue(valToWrite);
            }
            else
            {
                throw new JsonSerializationException(string.Format(SR.Object_Cannot_Be_Converted_To_SessionStateActions, "value"));
            }
        }

    }
}
