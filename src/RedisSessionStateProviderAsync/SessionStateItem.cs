using System;
using System.Web.SessionState;

namespace Microsoft.AspNet.SessionState
{
    class SessionStateItem
    {
        public TimeSpan? LockAge { get; set; }

        public int? LockCookie { get; set; }

        public bool? Locked { get; set; }

        public byte[] SessionItem { get; set; }

        public bool? Uninitialized { get; set; }
    }
}
