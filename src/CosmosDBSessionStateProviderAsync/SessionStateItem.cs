// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionState
{
    using Newtonsoft.Json;
    using System;
    using System.Web.SessionState;

    class SessionStateItem
    {
        [JsonProperty(PropertyName = "id", Required = Required.Always)]
        public string SessionId { get; set; }

        // in second
        [JsonProperty(PropertyName = "lockAge", Required = Required.AllowNull)]
        [JsonConverter(typeof(TimeSpanConverter))]
        public TimeSpan? LockAge { get; set; }

        [JsonProperty(PropertyName = "lockCookie", Required = Required.AllowNull)]
        public int? LockCookie { get; set; }

        //in sec
        // Leverage CosmosDB's TTL function to remove expired sessionstate item
        //ref https://docs.microsoft.com/en-us/azure/cosmos-db/time-to-live
        [JsonProperty(PropertyName = "ttl", Required = Required.AllowNull)]
        public int? Timeout { get; set; }

        [JsonProperty(PropertyName = "locked", Required = Required.AllowNull)]
        public bool? Locked { get; set; }

        [JsonProperty(PropertyName = "sessionItem", Required = Required.AllowNull)]
        public byte[] SessionItem { get; set; }

        [JsonProperty(PropertyName ="uninitialized", Required = Required.AllowNull)]
        [JsonConverter(typeof(SessionStateActionsConverter))]
        public SessionStateActions? Actions {get;set;}
    }
}
