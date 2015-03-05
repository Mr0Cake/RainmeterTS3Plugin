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
using System.Text;

namespace PluginEmpty
{
    public class TeamspeakConnectionThread
    {
        public Thread ConnectionThread;
        public TeamspeakConnectionThread()
        {
            startThread();
            Keep_Alive_Timer.Elapsed += time_Elapsed;
            chatTimer.Elapsed += chatTime_Elapsed;
            Retry_Connection_Timer.Elapsed += Reconnect;
        }

        public void startThread()
        {
            ConnectionThread = new Thread(Connect);
            ConnectionThread.Start();
        }
        
        public IntPtr SkinHandle;
        public string FinishAction;
        //public static Thread ConnectionThread;
        private static System.Timers.Timer Keep_Alive_Timer = new System.Timers.Timer(30000);
        private static System.Timers.Timer chatTimer = new System.Timers.Timer(60000);
        private static System.Timers.Timer Retry_Connection_Timer = new System.Timers.Timer(1000);
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
                lock (ThreadLocker)
                {
                    Connected = ConnectionState.Connecting;
                    QueryDispatcher = new AsyncTcpDispatcher("localhost", 25639);
                    QueryDispatcher.BanDetected += QueryDispatcher_BanDetected;
                    QueryDispatcher.ReadyForSendingCommands += QueryDispatcher_ReadyForSendingCommands;
                    QueryDispatcher.ServerClosedConnection += QueryDispatcher_ServerClosedConnection;
                    QueryDispatcher.SocketError += QueryDispatcher_SocketError;
                    QueryDispatcher.NotificationReceived += QueryDispatcher_NotificationReceived;
                    API.Log(API.LogType.Debug, "Teamspeak.ddl: linked events");
                    QueryDispatcher.Connect();
                    Keep_Alive_Timer.Start();
                }
                
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

        private void updateOutput(Object StateInfo = null)
        {
            API.Log(API.LogType.Debug, "Teamspeak.ddl: UpdateOutput");
            currentUser = QueryRunner.SendWhoAmI();
            if (!currentUser.IsErroneous)
            {
                string channelpath = QueryRunner.GetChannelConnectionInfo().Path;
                //look for / not preceded by \
                string[] paths = Regex.Split(channelpath, @"(?<![\\])/");

                ChannelName = paths[paths.Length - 1].Replace(@"\/", "/").Replace("[cspacer]", String.Empty).Trim();

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
            Retry_Connection_Timer.Start();
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
            ChannelClientList = new List<ClientListEntry>();
            talking.Clear();
        }

#region Events
        /// <summary>
        /// Event that fires every second to check if there is a connection available
        /// If there is no connection the interval will rise with 0.5s until 8s
        /// </summary>
        private void Reconnect(object sender, ElapsedEventArgs e)
        {
            Retry_Connection_Timer.Interval = Retry_Connection_Timer.Interval < 8000 ? Retry_Connection_Timer.Interval + 500 : 8000;
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
                API.Log(API.LogType.Debug, "Teamspeak.ddl: Reconnect");
            }
            
        }
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

            }
            else
            {
                if(!Retry_Connection_Timer.Enabled)
                    Retry_Connection_Timer.Start();
                Keep_Alive_Timer.Stop();
            }

        }

        /// <summary>
        /// Ready for sending commands
        /// </summary>
        private void QueryDispatcher_ReadyForSendingCommands(object sender, System.EventArgs e)
        {
            API.Log(API.LogType.Debug, "Teamspeak.ddl: Teamspeak Is running");
            if(!Keep_Alive_Timer.Enabled)
            Keep_Alive_Timer.Start();
            // you can only run commands on the queryrunner when this event has been raised first!
            try
            {
                QueryRunner = new QueryRunner(QueryDispatcher);
                //check if whoami returns a valid user, if erroneous teamspeak is not connected to a server
                //try connecting again later.
                if ((currentUser = QueryRunner.SendWhoAmI()).IsErroneous)
                {
                    API.Log(API.LogType.Debug, "Teamspeak.ddl: Failed to retreive currentUser: "+currentUser.ErrorId+" "+currentUser.ErrorMessage);
                    Disconnect();
                    return;
                }
                Retry_Connection_Timer.Stop();    
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
                API.Log(API.LogType.Debug, "Teamspeak.ddl: ClientMoved Event - "+e.ClientId+" "+e.TargetChannelId);
                bool remove = false;
                ClientListEntry toRemove = null;
                if (currentUser.ClientId == e.ClientId || e.TargetChannelId == currentUser.ChannelId )
                {
                    if (ThreadPool.QueueUserWorkItem(new WaitCallback(updateOutput)))
                    {
                        API.Log(API.LogType.Debug, "Teamspeak.ddl: UpdateOutput queued");
                    }
                    else
                    {
                        API.Log(API.LogType.Debug, "Teamspeak.ddl: Failed to queue UpdateOutput");
                    }
                    //queue the update so that we can get other notifications
                    //updateOutput();
                }
                else
                {
                    foreach (ClientListEntry channeluser in ChannelClientList)
                    {
                        if (e.ClientId == channeluser.ClientId)
                        {
                            API.Log(API.LogType.Debug, "Teamspeak.ddl: ClientMoved - Client is in channel");
                            if (e.TargetChannelId != currentUser.ChannelId)
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
        /// Removes brackets and text between it
        /// </summary>
        /// <param name="message">string to remove the brackets from</param>
        /// <returns></returns>
        private string ClearMessage(string message)
        {
            StringBuilder sb = new StringBuilder(message);

            int counter = 0;
            int pos = 0;
            int size = 0;
            char current = '\0';
            for (int i = 0; i < message.Length; i++)
            {
                current = message[i];
                if (current == '[')
                {
                    if (counter < 1)
                    {
                        pos += i;
                    }
                    counter++;
                }
                if (current == ']')
                {
                    if (counter > 0)
                    {
                        if (counter == 1)
                        {
                            sb.Remove(pos, ++size);
                            pos = 0-size;
                            counter = 0;
                            size = 0;
                        }
                        else
                        {
                            counter--;
                        }
                    }
                }
                if (counter > 0)
                    size++;
            }
                return sb.ToString();
        }
        /// <summary>
        /// Event MessageReceived
        /// Will change TextMessage to "nickname: message"
        /// starts a timer that will clear the message after 1 minute
        /// </summary>
        private void Notifications_MessageReceived(object sender, TS3QueryLib.Core.Server.Notification.EventArgs.MessageReceivedEventArgs e)
        {
            TextMessage = e.InvokerNickname + ":\r\n" + ClearMessage(e.Message);
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
                //Keep_Alive_Timer.Interval = Keep_Alive_Timer.Interval < 15000 ? Keep_Alive_Timer.Interval + 1000 : Keep_Alive_Timer.Interval;
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
                API.Log(API.LogType.Debug, "Teamspeak.ddl: ChannelTalkStatusEvent"+e.ClientId+" "+e.TalkStatus);
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
                        if (ThreadPool.QueueUserWorkItem(new WaitCallback(updateOutput)))
                        {
                            API.Log(API.LogType.Debug, "Teamspeak.ddl: Talking UpdateOutput queued");
                        }
                        else
                        {
                            API.Log(API.LogType.Debug, "Teamspeak.ddl: Talking Failed to queue UpdateOutput");
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
#endregion


    }
}
