using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Client
{
    static class Logger
    {

        private static readonly object _syncObject = new object();
        private static Queue<string> logItemQueue;
        private static Thread loggerThread;

        public static void StartLog()
        {
            logItemQueue = new Queue<string>();

            loggerThread = new Thread(log);
            loggerThread.Start();

        }

        public static void StopLog()
        {
            // Interrompo il thread per il logger
            lock (_syncObject)
            {
                // Metto in coda
                logItemQueue.Enqueue(null);
                // Sveglio il thread
                Monitor.Pulse(_syncObject);
            }
        }


        public static void log()
        {
            string element = null;
            while (true)
            {

                lock (_syncObject)
                {
                    while (logItemQueue.Count == 0)
                        Monitor.Wait(_syncObject);

                    element = logItemQueue.Dequeue();

                    if (element == null)
                        return;

                }
                StreamWriter fs = new StreamWriter("log.txt", true);

                fs.Write("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fffff") + "](" + Thread.CurrentThread.ManagedThreadId + ") " + element + "\n");
                fs.Close();

            }


            // only one thread can own this lock, so other threads
            // entering this method will wait here until lock is
            // available.
            //lock (_syncObject)
            //{
                
            //}
        }

        public static void Info(string msg)
        {
            lock (_syncObject)
            {
                // Metto in coda
                logItemQueue.Enqueue("[INFO] " + msg);
                // Sveglio il thread
                Monitor.Pulse(_syncObject);
            }
        }

        public static void Error(string msg)
        {
            lock (_syncObject)
            {
                // Metto in coda
                logItemQueue.Enqueue("[ERROR] " + msg);
                // Sveglio il thread
                Monitor.Pulse(_syncObject);
            }
        }

        public static void Debug(string msg)
        {
            if (Constants.DebugEnabled == true)
            {
                lock (_syncObject)
                {
                    // Metto in coda
                    logItemQueue.Enqueue("[DEBUG] " + msg);
                    // Sveglio il thread
                    Monitor.Pulse(_syncObject);
                }
            }
        }

    }
}
