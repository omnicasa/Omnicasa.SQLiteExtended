using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Omnicasa.SQLiteExtended.Events;
using SQLite;

namespace Omnicasa.SQLiteExtended.ConnectionManagement
{
    /// <summary>
    /// SQLite ConnectionManager for all sub project.
    /// </summary>
    public static class ConnectionManager
    {
#pragma warning disable SA1310
        private const int MAX_WRITE_CONNECTION = 4;
        private const int MAX_WRITE_CONNECTION_ALIVE = 2;
#pragma warning restore SA1310

        private static readonly object gettingReadConnectionLock = new ();
        private static readonly object gettingWriteConnectionLock = new ();

        private static bool releasing;

        /// <summary>
        /// Tracking database changed events
        /// </summary>
        public static event EventHandler<HookTrackingChangeTableEventArgs> TrackingChangeEvent;

        /// <summary>
        /// Database is busy
        /// </summary>
        public static bool IsBusy { get; set; }

        /// <summary>
        /// Network is online
        /// </summary>
        public static bool IsOnline { get; set; }

        /// <summary>
        /// Read connections
        /// </summary>
        public static List<SQLiteConnection> ReadConnections { get; set; } = new List<SQLiteConnection>();

        /// <summary>
        /// Read and Write connections
        /// </summary>
        public static List<SQLiteConnection> ReadWriteConnections { get; set; } = new List<SQLiteConnection>();

        /// <summary>
        /// Temporary connections
        /// </summary>
        private static List<SQLiteConnection> TemporaryConnections { get; set; } = new List<SQLiteConnection>();

        /// <summary>
        /// Get a connection to sqlite file path for read only
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static SQLiteConnection GetReadOnlyConnection(string filePath)
        {
            try
            {
                if (releasing)
                {
                    return null;
                }

                lock (gettingReadConnectionLock)
                {
                    const int maxReadConnections = 200;
                    const int cleanConnections = 20;
                    if (ReadConnections?.Count >= maxReadConnections)
                    {
                        for (int i = 0; i < cleanConnections; i++)
                        {
                            Dispose(ReadConnections[i]);
                        }

                        // Remove from list
                        ReadConnections?.RemoveRange(0, cleanConnections);

                        // Release temporary connections
                        ReleaseTemporaryConnections();
                    }
                }

                // Create connection to localDB
                var count = 0;
                var maxCount = 10;
                while (count++ <= maxCount)
                {
                    try
                    {
                        var connection = new SQLiteConnection(filePath, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
                        Task.Run(() =>
                        {
                            try
                            {
                                ReadConnections?.Add(connection);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine(ex);
                            }
                        });
                        return connection;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                }

                // Null
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return null;
            }
        }

        /// <summary>
        /// Get a connection to sqlite file path for read and write
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="isInBackground"></param>
        /// <returns></returns>
        public static SQLiteConnection GetReadWriteConnection(string filePath, bool isInBackground = false)
        {
            try
            {
                if (releasing)
                {
                    return null;
                }

                // Check Sync is working
                // Delay
                var delaySeconds = 200; // miliseconds
                var maxTimes = 50; // 50 times
                var times = 0;

                var numberOfProcessingConnection = ReadWriteConnections?
                    .Where(x => !x.IOError)
                    .Where(p => p.IsInTransaction || p.IsOpen);

                // Database is busy or connections is in a transaction ???
                while (IsBusy || (numberOfProcessingConnection != null && numberOfProcessingConnection.Count() >= MAX_WRITE_CONNECTION_ALIVE))
                {
                    if (times++ > maxTimes)
                    {
                        break;
                    }

                    Debug.WriteLine("Waiting to get a write connection...");
                    Thread.Sleep(delaySeconds);
                }

                // Dipose old write connection
                lock (gettingWriteConnectionLock)
                {
                    if (ReadWriteConnections != null && ReadWriteConnections.Any())
                    {
                        var totalConnection = ReadWriteConnections.Count;
                        if (totalConnection >= MAX_WRITE_CONNECTION)
                        {
                            var aliveConnections = new List<SQLiteConnection>();
                            for (int i = 0; i < totalConnection; i++)
                            {
                                var connection = ReadWriteConnections[i];
                                if (!connection.IsOpen || (connection.IsInTransaction && connection.IOError))
                                {
                                    Dispose(connection);
                                }
                                else
                                {
                                    aliveConnections.Add(connection);
                                }
                            }

                            ReadWriteConnections = new List<SQLiteConnection>(aliveConnections);
                        }
                    }
                }

                // Create a write connection
                var count = 0;
                const int maxCount = 10;
                while (count++ <= maxCount)
                {
                    try
                    {
                        var connection = new SQLiteConnection(filePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);

                        // App is in background
                        if (!isInBackground)
                        {
                            connection.TrackingChangeEventHandler += (sender, args) => { DelayInvokeHookTrackingChangeTableEvent.DelayInvoke(TrackingChangeEvent, args); };
                        }

                        // Adding
                        Task.Run(() =>
                        {
                            try
                            {
                                ReadWriteConnections?.Add(connection);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine(ex);
                            }
                        });

                        return connection;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                }

                // Null
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return null;
            }
        }

        /// <summary>
        /// Release all read and write connections
        /// </summary>
        public static void ReleaseAllConnections()
        {
            try
            {
                releasing = true;

                // Reset value
                SQLiteConnection.HasOptimizedConnection = false;

                // Release all read connections
                if (ReadConnections != null && ReadConnections.Any())
                {
                    foreach (var conn in ReadConnections)
                    {
                        Dispose(conn);
                    }

                    ReadConnections?.Clear();
                    GC.SuppressFinalize(ReadConnections);
                    ReadConnections = new List<SQLiteConnection>();
                }

                // Release all read/write connections
                if (ReadWriteConnections != null && ReadWriteConnections.Any())
                {
                    foreach (var conn in ReadWriteConnections)
                    {
                        Dispose(conn);
                    }

                    ReadWriteConnections?.Clear();
                    GC.SuppressFinalize(ReadWriteConnections);
                    ReadWriteConnections = new List<SQLiteConnection>();
                }

                // Release all temporary connections
                if (TemporaryConnections != null && TemporaryConnections.Any())
                {
                    // Importance: It can be loop forever
                    // Don't implenment new way
                    var count = TemporaryConnections.Count;
                    for (var i = 0; i < count; i++)
                    {
                        Dispose(TemporaryConnections[i]);
                    }

                    TemporaryConnections?.Clear();
                    GC.SuppressFinalize(TemporaryConnections);
                    TemporaryConnections = new List<SQLiteConnection>();
                }
            }
            catch (Exception ex)
            {
                ReadConnections = new List<SQLiteConnection>();
                ReadWriteConnections = new List<SQLiteConnection>();
                Debug.WriteLine(ex);
            }
            finally
            {
                releasing = false;
            }
        }

        /// <summary>
        /// Dipose connection
        /// </summary>
        /// <param name="connection"></param>
        public static void Dispose(SQLiteConnection connection)
        {
            Task.Run(() =>
            {
                try
                {
                    if (connection?.IsInTransaction == true && connection?.IOError == false)
                    {
                        TemporaryConnections?.Add(connection);
                        return;
                    }

                    connection?.Close();
                    connection?.Dispose();

                    connection = null;
                    GC.Collect();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    connection = null;
                }
            });
        }

        /// <summary>
        /// Dispose all connections in temporary
        /// </summary>
        private static void ReleaseTemporaryConnections()
        {
            Task.Run(() =>
            {
                try
                {
                    if (TemporaryConnections == null || !TemporaryConnections.Any())
                    {
                        return;
                    }

                    // Importance: It can be loop forever
                    // Don't implenment new way
                    var count = TemporaryConnections.Count;
                    for (var i = 0; i < count; i++)
                    {
                        Dispose(TemporaryConnections[i]);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            });
        }

        /// <summary>
        /// Delay invoke database changed event
        /// </summary>
        private static class DelayInvokeHookTrackingChangeTableEvent
        {
            private static readonly List<HookTrackingChangeTableEventArgs> trackingChangeTableEventArgs = new ();
            private static readonly ItemEqualityComparer itemEqualityComparer = new ();
            private static readonly object locker = new ();
            private static Timer timer;

            /// <summary>
            /// Delay invoke
            /// </summary>
            /// <param name="handler"></param>
            /// <param name="args"></param>
            public static void DelayInvoke(EventHandler<HookTrackingChangeTableEventArgs> handler, HookTrackingChangeTableEventArgs args)
            {
                try
                {
                    if (handler == null || args == null)
                    {
                        return;
                    }

                    // Add
                    lock (locker)
                    {
                        trackingChangeTableEventArgs.Add(args);
                    }

                    // Dispose timer
                    timer?.Dispose();
                    timer = null;

                    // Create new timer
                    timer = new Timer(
                        state =>
                        {
                            try
                            {
                                lock (locker)
                                {
                                    if (timer == null || trackingChangeTableEventArgs == null || !trackingChangeTableEventArgs.Any())
                                    {
                                        return;
                                    }
                                }

                                // Dispose timer
                                timer?.Dispose();
                                timer = null;

                                // Run
                                lock (locker)
                                {
                                    // Get changed tables
                                    var eventArgs = trackingChangeTableEventArgs.Distinct(itemEqualityComparer).ToList();
                                    trackingChangeTableEventArgs.Clear();

                                    // Invoke
                                    eventArgs.ForEach(p => handler.Invoke(null, p));
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine(ex);
                            }
                        },
                        null,
                        TimeSpan.FromMilliseconds(100),
                        TimeSpan.FromMilliseconds(3000));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }

            // Comparer
            private class ItemEqualityComparer : IEqualityComparer<HookTrackingChangeTableEventArgs>
            {
                public bool Equals(HookTrackingChangeTableEventArgs x, HookTrackingChangeTableEventArgs y)
                {
                    if (x == null || y == null)
                    {
                        return false;
                    }

                    // Two items are equal if their keys are equal.
                    return x.Table == y.Table && x.Type == y.Type;
                }

                public int GetHashCode(HookTrackingChangeTableEventArgs obj)
                {
                    return obj.Table.GetHashCode();
                }
            }
        }
    }
}
