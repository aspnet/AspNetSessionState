//------------------------------------------------------------------------------
// <copyright file="EtwTrace.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>                                                                
//------------------------------------------------------------------------------

using System;
using System.Diagnostics.Tracing;
using System.Web;

/*
 * EtwTraceType class
 */

namespace Microsoft.AspNet.SessionState
{
    [EventSource(Guid = "e195a708-06d5-4605-bbfe-818c9ff8e124",
        Name = "Microsoft-AspNet-SessionState-SessionStateAsyncModule")]
    internal class SessionEventSource : EventSource
    {
        private SessionEventSource()
        {
        }

        public static SessionEventSource Log { get; } = new SessionEventSource();

        [NonEvent]
        public void SessionDataBegin(HttpContext context)
        {
            if (IsEnabled() && context != null)
            {
                RaiseEvent(SessionDataBegin, context);
            }
        }

        [Event(42, Level = EventLevel.Informational, Keywords = Keywords.AspNetReq)]
        private void SessionDataBegin(Guid contextId)
        {
            WriteEvent(EventType.SessionDataBegin, contextId);
        }

        [NonEvent]
        public void SessionDataEnd(HttpContext context)
        {
            if (IsEnabled())
            {
                RaiseEvent(SessionDataEnd, context);
            }
        }


        [Event(43, Level = EventLevel.Informational, Keywords = Keywords.AspNetReq)]
        private void SessionDataEnd(Guid contextId)
        {
            if (IsEnabled())
            {
                WriteEvent(EventType.SessionDataEnd, contextId);
            }
        }

        [NonEvent]
        private void RaiseEvent(Action<Guid> action, HttpContext context)
        {
            HttpWorkerRequest workerRequest = GetWorkerRequest(context);
            if (workerRequest == null)
                return;

            action(workerRequest.RequestTraceIdentifier);
        }

        [NonEvent]
        private HttpWorkerRequest GetWorkerRequest(HttpContext context)
        {
            IServiceProvider serviceProvider = context;
            var workerRequest = (HttpWorkerRequest) serviceProvider.GetService(typeof (HttpWorkerRequest));

            return workerRequest;
        }

        public class EventType
        {
            internal static readonly int SessionDataBegin = 42;
            internal static readonly int SessionDataEnd = 43;
        }

        public class Keywords
        {
            public const EventKeywords AspNetReq = (EventKeywords) 1;
        }
    }
}