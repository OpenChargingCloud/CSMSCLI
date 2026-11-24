/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of CSMSCLI <https://github.com/OpenChargingCloud/CSMSCLI>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

namespace cloud.charging.open.ChargingStation.Tests
{

    /// <summary>
    /// What TestStations.cs, linked in from the station's tests, waits for:
    /// the patience of the station's TalkingToTheCSMSTests, whose tests are
    /// not wanted here.
    /// </summary>
    internal static class TalkingToTheCSMSTests
    {
        internal static readonly TimeSpan  Patience  = TimeSpan.FromSeconds(30);
    }

}
