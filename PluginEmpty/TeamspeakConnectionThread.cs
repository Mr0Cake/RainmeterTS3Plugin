using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Timers;
using System.Net.Sockets;
using TS3QueryLib.Core;
using TS3QueryLib.Core.Client;
using TS3QueryLib.Core.Client.Entities;
using TS3QueryLib.Core.Client.Responses;
using TS3QueryLib.Core.Client.Notification.EventArgs;
using TS3QueryLib.Core.Client.Notification.Enums;
using TS3QueryLib.Core.Common;
using TS3QueryLib.Core.Common.Responses;
using TS3QueryLib.Core.Communication;
using Rainmeter;
using System.Text.RegularExpressions;

namespace PluginEmpty
{
    public class TeamspeakConnectionThread
    {
        public Thread ConnectionThread;
        public TeamspeakConnectionThread()
        {
            startThread();
        }

        public void startThread()
        {
            ConnectionThread = new Thread(Connect);
            ConnectionThread.Start();
        }
        
        public IntPtr SkinHandle;
        public string FinishAction;
        //public static Thread ConnectionThread;
        private static System.Timers.Timer time = new System.Timers.Timer();
        private static System.Timers.Timer chatTimer = new System.Timers.Timer(60000);
        public readonly object ThreadLocker = new object();
        public ConnectionState Connected = ConnectionState.Disconnected;
        public enum ConnectionState
        {
            Connecting,
            Connected,
            Disconnected
        }
        public static AsyncTcpDispatcher QueryDispatcher { get; set; }

        public static QueryRunner QueryRunner { get; set; }

        public string ChannelName = "Not Connected";
        public string ChannelClients = "";
        public string WhoIsTalking = "";
        public string TextMessage = "";
        public TS3QueryLib.Core.Client.Responses.WhoAmIResponse currentUser { get; set; }
        public ListResponse<ChannelListEntry> channels { get; set; }
        public ListResponse<ClientListEntry> clients { get; set; }

        private List<ClientListEntry> ChannelClientList { get; set; }

        private uint CurrentChannelID = 0;

        private void Connect()
        {
            try
            {
                API.Log(API.LogType.Debug, "Teamspeak.ddl: Connecting");
                // do not connect when already connected or during connection establishing
                if (QueryDispatcher != null)
                {
                    API.Log(API.LogType.Debug, "Teamspeak.ddl: QueryDispatcher not null");
                    return;
                }
                time.Interval = 1000;
                time.Elapsed += time_Elapsed;
                chatTimer.Elapsed += chatTime_Elapsed;
                Connected = ConnectionState.Connecting;
                QueryDispatcher = new AsyncTcpDispatcher("localhost", 25639);
                QueryDispatcher.BanDetected += QueryDispatcher_BanDetected;
                QueryDispatcher.ReadyForSendingCommands += QueryDispatcher_ReadyForSendingCommands;
                QueryDispatcher.ServerClosedConnection += QueryDispatcher_ServerClosedConnection;
                QueryDispatcher.SocketError += QueryDispatcher_SocketError;
                QueryDispatcher.NotificationReceived += QueryDispatcher_NotificationReceived;
                QueryDispatcher.Connect();
            }
            catch (Exception e)
            {
                API.Log(API.LogType.Error, "Teamspeak.ddl: "+e.Message);
                Disconnect();
            }

        }

        private void chatTime_Elapsed(object sender, ElapsedEventArgs e)
        {
            TextMessage = "";
            chatTimer.Stop();
        }

        private void time_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (Connected == ConnectionState.Connected && !(currentUser = QueryRunner.SendWhoAmI()).IsErroneous)
            {
                currentUser = QueryRunner.SendWhoAmI();
                API.Log(API.LogType.Debug, "Teamspeak.ddl: server_keep_alive_message");
                //reset timer to 5 seconds
                if (time.Interval != 5000)
                    time.Interval = 5000;
            }
            else
            {
                API.Log(API.LogType.Debug, "Try Reconnect");
                //Try again when disconnected
                if (Connected != TeamspeakConnectionThread.ConnectionState.Connecting)
                {
                    ChannelName = "";
                    ChannelClients = "";
                    WhoIsTalking = "";
                    TextMessage = "";
                    if (QueryDispatcher != null)
                    {
                        QueryDispatcher.Disconnect();
                        QueryDispatcher.DetachAllEventListeners();
                    }

                    QueryDispatcher = null;
                    QueryRunner = null;
                    Connect();
                    API.Log(API.LogType.Debug, "Teamspeak.ddl: ReConnect");
                    
                }
            }

        }

        private void QueryDispatcher_ReadyForSendingCommands(object sender, System.EventArgs e)
        {
            
            API.Log(API.LogType.Debug, "Teamspeak.ddl: Teamspeak Is running");
            time.Start();
            // you can only run commands on the queryrunner when this event has been raised first!
            try { 
                QueryRunner = new QueryRunner(QueryDispatcher);
                int i = 0;
                char[] dots = {'\0','\0','\0'};
                do
                {
                    //is teamspeak connected to a server
                    currentUser = QueryRunner.SendWhoAmI();
                    ChannelName = "Connecting to server"+new string(dots);
                    API.Log(API.LogType.Debug, "Teamspeak.ddl: Checking ServerConnection Message: "+currentUser.ErrorMessage);

                    if (i <= 2)
                    {
                        dots[i] = '.';
                        i++;
                    }
                    else
                    {
                        dots[0] = '\0';
                        dots[1] = '\0';
                        dots[2] = '\0';
                        i = 0;
                    }
                    
                    System.Threading.Thread.Sleep(1000);
                } while (currentUser.IsErroneous);
                Connected = ConnectionState.Connected;
                API.Log(API.LogType.Debug, "Teamspeak.ddl: Connected to server");
                QueryRunner.Notifications.ChannelTalkStatusChanged += Notifications_ChannelTalkStatusChanged;
                QueryRunner.Notifications.ClientMoved += Notifications_ClientMoved;
                QueryRunner.Notifications.MessageReceived += Notifications_MessageReceived;
                QueryRunner.RegisterForNotifications(ClientNotifyRegisterEvent.Any);
                API.Log(API.LogType.Debug, "Teamspeak.ddl: Registered for events");
                updateOutput();
            }
            catch (Exception d)
            {
                API.Log(API.LogType.Error, "Teamspeak.ddl: " + d.Message);
                Disconnect();
            }
        }
        private void updateChannelClientsStringOutput()
        {
            lock (ThreadLocker)
            {
                ChannelClients = "";
                foreach (ClientListEntry channelclient in ChannelClientList)
                {
                    ChannelClients += channelclient.Nickname + Environment.NewLine;
                }
            }
        }

        private void updateOutput()
        {
            API.Log(API.LogType.Debug, "Teamspeak.ddl: UpdateOutput");
            currentUser = QueryRunner.SendWhoAmI();
            if (!currentUser.IsErroneous)
            {
                string channelpath = QueryRunner.GetChannelConnectionInfo().Path;
                //look for / not preceded by \
                string[] paths = Regex.Split(channelpath, @"(?<![\\])/");

                ChannelName = paths[paths.Length - 1].Replace(@"\/", "/");
                clients = QueryRunner.GetClientList();

                lock (ThreadLocker)
                {
                    ChannelClientList = new List<ClientListEntry>();
                    ChannelClients = "";
                    foreach (ClientListEntry client in clients.Values)
                    {
                        if (client.ChannelId == currentUser.ChannelId)
                        {
                            ChannelClientList.Add(client);
                            ChannelClients += (client.Nickname + Environment.NewLine);
                        }
                    }

                }


                API.Log(API.LogType.Debug, "Teamspeak.ddl: UpdateOutput" + "\r\n" + ChannelName + "\r\n" + ChannelClients);
            }
            else
            {
                API.Log(API.LogType.Debug, "Teamspeak.ddl: UpdateOutput CurrentUser Error");
                Disconnect();
            }
        }
        /// <summary>
        /// If a client moves on the server this event will fire.
        /// This checks if the user in the event is a user in your channel and will either remove or update
        /// </summary>
        private void Notifications_ClientMoved(object sender, TS3QueryLib.Core.Server.Notification.EventArgs.ClientMovedEventArgs e)
        {
            lock (ThreadLocker)
            {
                bool remove = false;
                ClientListEntry toRemove = null;
                if (currentUser.ClientId == e.ClientId || e.TargetChannelId == CurrentChannelID)
                {
                    updateOutput();
                }
                else
                {
                    foreach (ClientListEntry channeluser in ChannelClientList)
                    {
                        if (e.ClientId == channeluser.ClientId)
                        {
                            API.Log(API.LogType.Debug, "Teamspeak.ddl: ClientMoved - Client is in channel");
                            if (e.TargetChannelId != CurrentChannelID)
                            {
                                remove = true;
                                toRemove = channeluser;
                            }
                            break;
                        }
                    }
                    if (remove)
                    {
                        API.Log(API.LogType.Debug, "Teamspeak.ddl: ClientMoved - ClientRemoved: "+toRemove.Nickname);
                        ChannelClientList.Remove(toRemove);
                        updateChannelClientsStringOutput();
                    }
                }
            }

        }

        private void Notifications_MessageReceived(object sender, TS3QueryLib.Core.Server.Notification.EventArgs.MessageReceivedEventArgs e)
        {
            TextMessage = e.InvokerNickname + ":\r\n" + e.Message;
            chatTimer.Start();
        }

        private void QueryDispatcher_ServerClosedConnection(object sender, System.EventArgs e)
        {
            // this event is raised when the connection to the server is lost.
            Connected = ConnectionState.Disconnected;
            ClearValues();
        }

        private void QueryDispatcher_BanDetected(object sender, EventArgs<SimpleResponse> e)
        {
            Connected = ConnectionState.Disconnected;
            ChannelName = "Banned";
        }

        private void QueryDispatcher_SocketError(object sender, SocketErrorEventArgs e)
        {
            ClearValues();
            // do not handle connection lost errors because they are already handled by QueryDispatcher_ServerClosedConnection
            if (e.SocketError == SocketError.ConnectionReset)
                return;

            if (e.SocketError == SocketError.ConnectionRefused) 
            {
                API.Log(API.LogType.Error, "Teamspeak.ddl: socket error, Teamspeak Not Running");
                //increment reconnect timer with 1s for each error received
                time.Interval = time.Interval < 15000 ? time.Interval + 1000 : time.Interval;
                
            }
            // this event is raised when a socket exception has occured
            API.Log(API.LogType.Error, "Teamspeak.ddl: socket error" + e.SocketError.ToString());
            // force disconnect
            Disconnect();
        }

        private void QueryDispatcher_NotificationReceived(object sender, EventArgs<string> e)
        {

        }
        private Dictionary<uint, string> talking = new Dictionary<uint, string>();
        
        private void Notifications_ChannelTalkStatusChanged(object sender, TalkStatusEventArgsBase e)
        {
            lock (ThreadLocker)
            {
                if (talking.ContainsKey(e.ClientId))
                {
                    if (e.TalkStatus == TalkStatus.TalkFinished)
                        talking.Remove(e.ClientId);
                }
                else
                {
                    if (e.TalkStatus == TalkStatus.TalkStarted)
                    {
                        foreach (ClientListEntry cle in ChannelClientList)
                        {
                            if (e.ClientId == cle.ClientId)
                            {
                                talking.Add(e.ClientId, cle.Nickname);
                            }
                        }
                    }
                }


                WhoIsTalking = "";

                foreach (KeyValuePair<uint, string> kvp in talking)
                {
                    WhoIsTalking += kvp.Value + " ";
                }
            }

        }

        public void Disconnect()
        {
            // QueryRunner disposes the Dispatcher too
            //if (QueryRunner != null)
            //    QueryRunner.Dispose();
            if (QueryDispatcher != null)
            {
                QueryDispatcher.Disconnect();
                QueryDispatcher.DetachAllEventListeners();
            }

            //clear values
            ClearValues();
            QueryDispatcher = null;
            QueryRunner = null;
            Connected = ConnectionState.Disconnected;
        }

        private void ClearValues(bool ch = false)
        {
            
            ChannelName = "Not connected.";
            ChannelClients = "";
            WhoIsTalking = "";
            TextMessage = "";
            CurrentChannelID = 0;
            ChannelClientList = new List<ClientListEntry>();
            talking.Clear();
        }

    }
}
