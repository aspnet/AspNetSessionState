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
            modelBuilder.Entity<Session>().Property(p => p.LockDate).IsConcurrencyToken();
        }
    }
}
