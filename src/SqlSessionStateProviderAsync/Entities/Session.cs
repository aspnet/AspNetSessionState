// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionState.Entities
{
    using System;
    using System.ComponentModel.DataAnnotations;

    class Session
    {
        [Key, StringLength(88)]
        public string SessionId { get; set; }
        public DateTime Created { get; set; }
        public DateTime Expires { get; set; }
        public DateTime LockDate { get; set; }
        public int LockCookie { get; set; }
        public bool Locked { get; set; }
        [MaxLength]
        public byte[] SessionItem { get; set; }
        public int Flags { get; set; }
        public int Timeout { get; set; }
    }
}
