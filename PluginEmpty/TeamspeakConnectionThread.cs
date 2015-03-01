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

        /// <summary>
        /// reset values
        /// </summary>
        private void ClearValues()
        {
            ChannelName = "Not connected.";
            ChannelClients = "";
            WhoIsTalking = "";
            TextMessage = "";
            CurrentChannelID = 0;
            ChannelClientList = new List<ClientListEntry>();
            talking.Clear();
        }

#region Events
        /// <summary>
        /// This event marks the end of displaying the chatmessage.
        /// </summary>
        private void chatTime_Elapsed(object sender, ElapsedEventArgs e)
        {
            TextMessage = "";
            chatTimer.Stop();
        }

        /// <summary>
        /// This will keep the connection to the telnet server alive
        /// Checks if you are connected to teamspeak and to a server and will reset the timer
        /// else
        /// Try reconnecting
        /// </summary>
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
                    ClearValues();
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

        /// <summary>
        /// Ready for sending commands
        /// </summary>
        private void QueryDispatcher_ReadyForSendingCommands(object sender, System.EventArgs e)
        {
            API.Log(API.LogType.Debug, "Teamspeak.ddl: Teamspeak Is running");
            time.Start();
            // you can only run commands on the queryrunner when this event has been raised first!
            try
            {
                QueryRunner = new QueryRunner(QueryDispatcher);
                int i = 0;
                char[] dots = { '\0', '\0', '\0' };

                do
                {
                    //is teamspeak connected to a server
                    currentUser = QueryRunner.SendWhoAmI();
                    ChannelName = "Connecting to server" + new string(dots);
                    API.Log(API.LogType.Debug, "Teamspeak.ddl: Checking ServerConnection Message: " + currentUser.ErrorMessage);
                    //some code to display dots
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
                        API.Log(API.LogType.Debug, "Teamspeak.ddl: ClientMoved - ClientRemoved: " + toRemove.Nickname);
                        ChannelClientList.Remove(toRemove);
                        updateChannelClientsStringOutput();
                    }
                }
            }

        }

        /// <summary>
        /// Event MessageReceived
        /// Will change TextMessage to "nickname: message"
        /// starts a timer that will clear the message after 1 minute
        /// </summary>
        private void Notifications_MessageReceived(object sender, TS3QueryLib.Core.Server.Notification.EventArgs.MessageReceivedEventArgs e)
        {
            TextMessage = e.InvokerNickname + ":\r\n" + e.Message;
            chatTimer.Start();
        }

        /// <summary>
        /// this event is raised when the connection to the server is lost.
        /// </summary>
        private void QueryDispatcher_ServerClosedConnection(object sender, System.EventArgs e)
        {
            Disconnect();
        }
        /// <summary>
        /// You have been banned from this server
        /// </summary>
        private void QueryDispatcher_BanDetected(object sender, EventArgs<SimpleResponse> e)
        {
            ChannelName = "Banned";
            Disconnect();
        }

        /// <summary>
        /// This event is raised when a socket exception has occured
        /// ConnectionRefused:
        /// Teamspeak is not running
        /// Increment reconnect timer time Interval with 1 second until 15 seconds
        /// Disconnect
        /// other:
        /// Disconnect
        /// </summary>
        private void QueryDispatcher_SocketError(object sender, SocketErrorEventArgs e)
        {
            ClearValues();
            // do not handle connection lost errors because they are already handled by QueryDispatcher_ServerClosedConnection
            if (e.SocketError == SocketError.ConnectionReset)
                return;

            if (e.SocketError == SocketError.ConnectionRefused)
            {
                API.Log(API.LogType.Error, "Teamspeak.ddl: socket error, Teamspeak Not Running");
                time.Interval = time.Interval < 15000 ? time.Interval + 1000 : time.Interval;
            }
            else
            {
                API.Log(API.LogType.Error, "Teamspeak.ddl: socket error" + e.SocketError.ToString());
            }

            Disconnect();
        }

        /// <summary>
        /// This event will bind to all notifications.
        /// You can display all the events using 'e'
        /// </summary>
        private void QueryDispatcher_NotificationReceived(object sender, EventArgs<string> e)
        {

        }
        

        /// <summary>
        /// This event is raised when a client starts or stops talking
        /// If Client ID is in talking and talkFinished -> remove from talking
        /// If Client ID is not in talking and TalkStarted -> check for id in ChannelClientList
        /// If ID is not found in ChannelClientList that means we have a user that was not added correctly
        /// This might happen when someone joins your channel at the same time.
        /// If this happens -> update all users.
        /// </summary>
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
                    bool found = false;
                    if (e.TalkStatus == TalkStatus.TalkStarted)
                    {
                        foreach (ClientListEntry cle in ChannelClientList)
                        {
                            if (e.ClientId == cle.ClientId)
                            {
                                talking.Add(e.ClientId, cle.Nickname);
                                found = true;
                                break;
                            }
                        }
                    }
                    if (!found)
                    {
                        //this means someone in your channel is not in the channelClientList but is talking
                        //-> ChannelClientList is not complete
                        API.Log(API.LogType.Debug, "Teamspeak.ddl: Talking client not in ChannelList");
                        updateOutput();
                    }
                }


                WhoIsTalking = "";

                foreach (KeyValuePair<uint, string> kvp in talking)
                {
                    WhoIsTalking += kvp.Value + " ";
                }
            }

        }
#endregion


    }
}
