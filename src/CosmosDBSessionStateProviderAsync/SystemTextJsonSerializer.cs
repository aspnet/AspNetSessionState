// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using Microsoft.Azure.Cosmos;
    using System.IO;
    using System.Text.Json;

    internal class SystemTextJsonSerializer : CosmosSerializer
    {
        private JsonSerializerOptions _opts;

        public SystemTextJsonSerializer(JsonSerializerOptions jsonSerializerOptions)
        {
            _opts = jsonSerializerOptions ?? default(JsonSerializerOptions);
        }

        public override T FromStream<T>(Stream stream)
        {
            using (stream)
            {
                if (stream.CanSeek
                       && stream.Length == 0)
                {
                    return default;
                }

                if (typeof(Stream).IsAssignableFrom(typeof(T)))
                {
                    return (T)(object)stream;
                }

                return JsonSerializer.Deserialize<T>(stream, _opts);
            }
        }

        public override Stream ToStream<T>(T input)
        {
            MemoryStream ms = new MemoryStream();
            JsonSerializer.Serialize(ms, input, input.GetType(), _opts);
            ms.Position = 0;
            return ms;
        }
    }
}
