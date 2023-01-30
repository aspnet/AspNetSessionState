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

            // See the note below in the writer for how to map this flags enum from a boolean.
            return reader.GetBoolean() ? SessionStateActions.InitializeItem : SessionStateActions.None;
        }

        public override void Write(Utf8JsonWriter writer, SessionStateActions? action, JsonSerializerOptions options)
        {
            if (action == null)
            {
                writer.WriteNullValue();
                return;
            }

            // SessionStateActions is a [Flags] enum. The SQL providers serialize it as flags. I don't know why we didn't
            //      just go with int or something flag-like here instead of true/false.
            // 'None' means that the initialization of this state item has already been done and
            //      thus the item is considered initialized. We serialize this item with the field name
            //      "uninitialized", so 'None' should map to false.
            writer.WriteBooleanValue((action != SessionStateActions.None));
        }
    }
}
