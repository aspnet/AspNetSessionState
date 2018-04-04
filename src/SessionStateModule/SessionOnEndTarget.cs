// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using System;
    using System.Web.SessionState;

    /*
     * Calls the OnSessionEnd event. We use an object other than the SessionStateModule
     * because the state of the module is unknown - it could have been disposed
     * when a session ends.
     */

    class SessionOnEndTarget
    {
        private int _sessionEndEventHandlerCount;

        public int SessionEndEventHandlerCount
        {
            get { return _sessionEndEventHandlerCount; }
            set { _sessionEndEventHandlerCount = value; }
        }

        public void RaiseOnEnd(HttpSessionStateContainer sessionStateContainer)
        {
            if (_sessionEndEventHandlerCount > 0)
            {
                SessionStateUtility.RaiseSessionEnd(sessionStateContainer, this, EventArgs.Empty);
            }
        }

        public void RaiseSessionOnEnd(string id, SessionStateStoreData item)
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