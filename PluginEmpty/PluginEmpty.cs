// Uncomment these only if you want to export GetString() or ExecuteBang().
#define DLLEXPORT_GETSTRING
//#define DLLEXPORT_EXECUTEBANG

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Rainmeter;
using System.Threading;



// Overview: This is a blank canvas on which to build your plugin.

// Note: Measure.GetString, Plugin.GetString, Measure.ExecuteBang, and
// Plugin.ExecuteBang have been commented out. If you need GetString
// and/or ExecuteBang and you have read what they are used for from the
// SDK docs, uncomment the function(s). Otherwise leave them commented out
// (or get rid of them)!qsdf

namespace PluginEmpty
{
    internal class Measure
    {
        public string RequestedStringValue;
        public IntPtr SkinHandle;
        public TeamspeakConnectionThread teamspeakConnection = new TeamspeakConnectionThread();
        private System.Timers.Timer retryTimer = new System.Timers.Timer(5000);

        internal Measure()
        {
        }

        internal void Reload(Rainmeter.API api, ref double maxValue)
        {
            SkinHandle = api.GetSkin();
            retryTimer.Elapsed += tick;
            retryTimer.Start();
        }

        internal double Update()
        {
            return 0.0;
        }


        //events
        private void tick(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (teamspeakConnection.Connected != TeamspeakConnectionThread.ConnectionState.Connecting && teamspeakConnection.Connected != TeamspeakConnectionThread.ConnectionState.Connected)
            {
                if (teamspeakConnection.ConnectionThread == null || teamspeakConnection.ConnectionThread.ThreadState == ThreadState.Stopped)
                {
                    if (TeamspeakConnectionThread.QueryDispatcher != null)
                    {
                        
                    }
                    teamspeakConnection.startThread();
                    API.Log(API.LogType.Debug, "Teamspeak.ddl: Start new thread");
                }
            }
            else
            {
                retryTimer.Stop();
            }
        }
        
        
        
#if DLLEXPORT_GETSTRING
        internal string GetString()
        {
            string output = "not connected";
            if (teamspeakConnection.Connected == TeamspeakConnectionThread.ConnectionState.Connected)
            {
                 output = teamspeakConnection.ChannelName +
                    "\r\n" + teamspeakConnection.ChannelClients +
                    "\r\n" + teamspeakConnection.WhoIsTalking +
                    "\r\n\r\n" + teamspeakConnection.TextMessage;
            }
            else
            {
                if (teamspeakConnection.Connected != TeamspeakConnectionThread.ConnectionState.Connecting)
                {
                    if (teamspeakConnection.ConnectionThread == null || teamspeakConnection.ConnectionThread.ThreadState == ThreadState.Stopped)
                    {
                        if (!retryTimer.Enabled)
                        {
                            retryTimer.Start();
                        }
                    }
                }
                else
                {
                    output = "connecting";
                }
            }
            return output;
            
        }
#endif
        
#if DLLEXPORT_EXECUTEBANG
        internal void ExecuteBang(string args)
        {
        }
#endif
    }

    public static class Plugin
    {
#if DLLEXPORT_GETSTRING
        static IntPtr StringBuffer = IntPtr.Zero;
#endif

        [DllExport]
        public static void Initialize(ref IntPtr data, IntPtr rm)
        {
            data = GCHandle.ToIntPtr(GCHandle.Alloc(new Measure()));
        }

        [DllExport]
        public static void Finalize(IntPtr data)
        {
            GCHandle.FromIntPtr(data).Free();
            
#if DLLEXPORT_GETSTRING
            if (StringBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(StringBuffer);
                StringBuffer = IntPtr.Zero;
            }
#endif
        }

        [DllExport]
        public static void Reload(IntPtr data, IntPtr rm, ref double maxValue)
        {
            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            measure.Reload(new Rainmeter.API(rm), ref maxValue);
        }

        [DllExport]
        public static double Update(IntPtr data)
        {
            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            return measure.Update();
        }
        
#if DLLEXPORT_GETSTRING
        [DllExport]
        public static IntPtr GetString(IntPtr data)
        {
            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            if (StringBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(StringBuffer);
                StringBuffer = IntPtr.Zero;
            }

            string stringValue = measure.GetString();
            if (stringValue != null)
            {
                StringBuffer = Marshal.StringToHGlobalUni(stringValue);
            }

            return StringBuffer;
        }
#endif

#if DLLEXPORT_EXECUTEBANG
        [DllExport]
        public static void ExecuteBang(IntPtr data, IntPtr args)
        {
            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            measure.ExecuteBang(Marshal.PtrToStringUni(args));
        }
#endif
    }
}
