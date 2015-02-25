using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Timers;
using System.Net.Sockets;
using TS3QueryLib.Core;
using TS3QueryLib.Core.Client;
using TS3QueryLib.Core.Client.Entities;
using TS3QueryLib.Core.Client.Notification.EventArgs;
using TS3QueryLib.Core.Client.Notification.Enums;
using TS3QueryLib.Core.Common;
using TS3QueryLib.Core.Common.Responses;
using TS3QueryLib.Core.Communication;
using Rainmeter;

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

        

        private void Connect()
        {
            API.Log(API.LogType.Debug, "Teamspeak.ddl: Connecting");
            // do not connect when already connected or during connection establishing
            if (QueryDispatcher != null)
            {
                API.Log(API.LogType.Debug, "Teamspeak.ddl: QueryDispatcher not null");
                return;
            }
            time.Interval = 10000;
            time.Elapsed += time_Elapsed;
            Connected = ConnectionState.Connecting;
            QueryDispatcher = new AsyncTcpDispatcher("localhost", 25639);
            QueryDispatcher.BanDetected += QueryDispatcher_BanDetected;
            QueryDispatcher.ReadyForSendingCommands += QueryDispatcher_ReadyForSendingCommands;
            QueryDispatcher.ServerClosedConnection += QueryDispatcher_ServerClosedConnection;
            QueryDispatcher.SocketError += QueryDispatcher_SocketError;
            QueryDispatcher.NotificationReceived += QueryDispatcher_NotificationReceived;
            QueryDispatcher.Connect();

        }

        private void time_Elapsed(object sender, ElapsedEventArgs e)
        {
            //QueryRunner = new QueryRunner(QueryDispatcher);
            if (Connected == ConnectionState.Connected)
            {
                currentUser = QueryRunner.SendWhoAmI();
                API.Log(API.LogType.Debug, "Teamspeak.ddl: time_elapsed "+currentUser.ErrorId);
            }
            else
            {
                
                //Try again when disconnected
                if (Connected != TeamspeakConnectionThread.ConnectionState.Connecting)
                {
                    ChannelName = "";
                    ChannelClients = "";
                    WhoIsTalking = "";
                    TextMessage = "";
                    if (QueryRunner != null)
                        QueryRunner.Dispose();

                    QueryDispatcher = null;
                    QueryRunner = null;
                    Connect();
                    API.Log(API.LogType.Debug, "Teamspeak.ddl: ReConnect");
                    
                }
            }

        }

        private void QueryDispatcher_ReadyForSendingCommands(object sender, System.EventArgs e)
        {
            Connected = ConnectionState.Connected;
            API.Log(API.LogType.Debug, "Teamspeak.ddl: Connected");
            time.Start();
            // you can only run commands on the queryrunner when this event has been raised first!
            QueryRunner = new QueryRunner(QueryDispatcher);
            do
            {
                currentUser = QueryRunner.SendWhoAmI();
                System.Threading.Thread.Sleep(1000);
            } while (currentUser.ErrorMessage == "not connected");
            QueryRunner.Notifications.ChannelTalkStatusChanged += Notifications_ChannelTalkStatusChanged;
            QueryRunner.Notifications.ClientMoved += Notifications_ClientMoved;
            QueryRunner.Notifications.MessageReceived += Notifications_MessageReceived;
            QueryRunner.RegisterForNotifications(ClientNotifyRegisterEvent.Any);
            updateOutput();
        }

        private void updateOutput()
        {
            currentUser = QueryRunner.SendWhoAmI();
            channels = QueryRunner.GetChannelList();
            clients = QueryRunner.GetClientList();

            foreach (ChannelListEntry cle in channels.Values)
            {
                if (currentUser.ChannelId == cle.ChannelId)
                {
                    ChannelName = cle.Name;
                    ChannelClients = "";
                    foreach (ClientListEntry client in clients.Values)
                    {
                        if (client.ChannelId == currentUser.ChannelId)
                        {
                            ChannelClients += ("\t"+client.Nickname + Environment.NewLine);
                        }
                    }
                    break;

                }
            }
            API.Log(API.LogType.Debug, "Teamspeak.ddl: UpdateOutput"+"\r\n"+ChannelName+"\r\n"+ChannelClients);

        }

        private void Notifications_ClientMoved(object sender, TS3QueryLib.Core.Server.Notification.EventArgs.ClientMovedEventArgs e)
        {
            updateOutput();
        }

        private void Notifications_MessageReceived(object sender, TS3QueryLib.Core.Server.Notification.EventArgs.MessageReceivedEventArgs e)
        {
            TextMessage = e.InvokerNickname + ":\r\n" + e.Message;
        }

        private void QueryDispatcher_ServerClosedConnection(object sender, System.EventArgs e)
        {
            // this event is raised when the connection to the server is lost.
            Connected = ConnectionState.Disconnected;
            ChannelName = "";
            ChannelClients = "";
            WhoIsTalking = "";
            TextMessage = "";
        }

        private void QueryDispatcher_BanDetected(object sender, EventArgs<SimpleResponse> e)
        {
            Connected = ConnectionState.Disconnected;
            ChannelName = "Banned";
        }

        private void QueryDispatcher_SocketError(object sender, SocketErrorEventArgs e)
        {
            ChannelName = "";
            ChannelClients = "";
            WhoIsTalking = "";
            TextMessage = "";
            // do not handle connection lost errors because they are already handled by QueryDispatcher_ServerClosedConnection
            if (e.SocketError == SocketError.ConnectionReset)
                return;

            // this event is raised when a socket exception has occured
            // API.Log(API.LogType.Error, "Teamspeak.ddl: SocketError " + e.SocketError);

            API.Log(API.LogType.Error, "Teamspeak.ddl: socket error" + e.SocketError.ToString());
            // force disconnect
            Disconnect();
        }

        private void QueryDispatcher_NotificationReceived(object sender, EventArgs<string> e)
        {

        }
        public Dictionary<uint, string> talking = new Dictionary<uint, string>();
        private void Notifications_ChannelTalkStatusChanged(object sender, TalkStatusEventArgsBase e)
        {

            if (e.TalkStatus == TalkStatus.TalkStarted)
            {
                foreach (ClientListEntry cle in clients)
                {
                    if (e.ClientId == cle.ClientId)
                    {
                        if (!talking.ContainsKey(cle.ClientId))
                            talking.Add(e.ClientId, cle.Nickname);
                        break;
                    }

                }

            }
            else
            {
                talking.Remove(e.ClientId);
            }

            WhoIsTalking = "";
            foreach (KeyValuePair<uint, string> kvp in talking)
            {
                WhoIsTalking += kvp.Value + " ";
            }

        }

        public void Disconnect()
        {
            // QueryRunner disposes the Dispatcher too
            if (QueryRunner != null)
                QueryRunner.Dispose();
            ChannelName = "";
            ChannelClients = "";
            WhoIsTalking = "";
            TextMessage = "";
            QueryDispatcher = null;
            QueryRunner = null;
            Connected = ConnectionState.Disconnected;
        }

    }
}
