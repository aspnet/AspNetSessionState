// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.AspNet.SessionState.AsyncProviders.SqlSessionState.Entities
{
    internal class SessionContext : DbContext
    {
        public virtual DbSet<Session> Sessions { get; set; }

        public SessionContext(string nameOrConnectionString)
            : base(nameOrConnectionString)
        {
        }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            // Make LockDate field as concurrency token
            modelBuilder.Entity<Session>().Property(p => p.LockDate).IsConcurrencyToken();
        }
    }
}
