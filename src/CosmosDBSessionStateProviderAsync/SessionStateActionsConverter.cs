// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState {
    using System;
    using System.Web.SessionState;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    class SessionStateActionsConverter : JsonConverter<SessionStateActions?>
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeof(SessionStateActions?) == typeToConvert;
        }

        public override SessionStateActions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return new Nullable<SessionStateActions>();
            }

            if (reader.TokenType != JsonTokenType.True && reader.TokenType != JsonTokenType.False)
            {
                throw new ArgumentException("reader");
            }

            return reader.GetBoolean() ? SessionStateActions.InitializeItem : SessionStateActions.None;
        }

        public override void Write(Utf8JsonWriter writer, SessionStateActions? action, JsonSerializerOptions options)
        {
            if (action == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteBooleanValue((action == SessionStateActions.None));
        }
    }
}
