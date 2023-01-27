// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using System;
    using System.Web.SessionState;
    using System.Text.Json.Serialization;

    class SessionStateItem
    {
        [JsonPropertyName("id")]
        [JsonRequired]
        public string SessionId { get; set; }

        // in second
        [JsonPropertyName("lockAge")]
        [JsonConverter(typeof(TimeSpanConverter))]
        public TimeSpan? LockAge { get; set; }

        [JsonPropertyName("lockCookie")]
        public int? LockCookie { get; set; }

        //in sec
        // Leverage CosmosDB's TTL function to remove expired sessionstate item
        //ref https://docs.microsoft.com/en-us/azure/cosmos-db/time-to-live
        [JsonPropertyName("ttl")]
        public int? Timeout { get; set; }

        [JsonPropertyName("locked")]
        public bool? Locked { get; set; }

        [JsonPropertyName("sessionItem")]
        public byte[] SessionItem { get; set; }

        [JsonPropertyName("uninitialized")]
        [JsonConverter(typeof(SessionStateActionsConverter))]
        public SessionStateActions? Actions {get;set;}
    }
}
