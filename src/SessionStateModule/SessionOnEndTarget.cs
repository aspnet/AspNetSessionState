// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionState
{
    using System;
    using System.Web.SessionState;

    /*
     * Calls the OnSessionEnd event. We use an object other than the SessionStateModule
     * because the state of the module is unknown - it could have been disposed
     * when a session ends.
     */

    internal class SessionOnEndTarget
    {
        private int _sessionEndEventHandlerCount;

        internal int SessionEndEventHandlerCount
        {
            get { return _sessionEndEventHandlerCount; }
            set { _sessionEndEventHandlerCount = value; }
        }

        internal void RaiseOnEnd(HttpSessionStateContainer sessionStateContainer)
        {
            if (_sessionEndEventHandlerCount > 0)
            {
                SessionStateUtility.RaiseSessionEnd(sessionStateContainer, this, EventArgs.Empty);
            }
        }

        internal void RaiseSessionOnEnd(string id, SessionStateStoreData item)
        {
            var sessionStateContainer = new HttpSessionStateContainer(
                id,
                item.Items,
                item.StaticObjects,
                item.Timeout,
                false,
                SessionStateModuleAsync.ConfigCookieless,
                SessionStateModuleAsync.ConfigMode,
                true);

            RaiseOnEnd(sessionStateContainer);
        }
    }
}