using System;
using System.IO;
using System.Threading;

namespace Client
{
    static class Logger
    {

        private static readonly object _syncObject = new object();


        public static void log(string msg)
        {
            // only one thread can own this lock, so other threads
            // entering this method will wait here until lock is
            // available.
            lock (_syncObject)
            {
                StreamWriter fs = new StreamWriter("log.txt", true);

                fs.Write("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fffff") + "](" + Thread.CurrentThread.ManagedThreadId + ") " + msg + "\n");
                fs.Close();
            }
        }

        public static void Info(string msg)
        {
            Logger.log("[INFO] " + msg);
        }

        public static void Error(string msg)
        {
            Logger.log("[ERROR] " + msg);
        }

        public static void Debug(string msg)
        {
            if (Constants.DebugEnabled == true)
                Logger.log("[DEBUG] " + msg);
        }

    }
}
