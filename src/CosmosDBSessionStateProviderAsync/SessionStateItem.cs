using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.AspNet.SessionState
{
    class SessionStateItem
    {
        [JsonProperty(PropertyName = "id", Required = Required.Always)]
        public string SessionId { get; set; }

        [JsonProperty(PropertyName = "partitionKey", Required = Required.AllowNull)]
        public string PartitionKey { get; set; }

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
    }
}
