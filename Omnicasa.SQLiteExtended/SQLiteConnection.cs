using System;
using System.Collections.Generic;
using SQLite;
using SQLiteLibrary.Events;

namespace SQLiteLibrary
{
    /// <summary>Represents an open connection to a SQLite database.</summary>
    public class SQLiteConnection : SQLite.SQLiteConnection, IConnection
    {
        /// <summary>
        /// Optimize the connections to database
        /// </summary>
        public static bool HasOptimizedConnection { get; set; }

        private static object lockInitialize = new ();

        private bool open;

        /// <inheritdoc/>
        public long ConnectionKey { get; set; } = 0;

        /// <inheritdoc/>
        public bool IOError { get; set; } = false;

        /// <summary>
        /// Is open connection
        /// </summary>
        public bool IsOpen => open;

        /// <summary>
        /// TrackingChangeEventHandler
        /// </summary>
        public event EventHandler<HookTrackingChangeTableEventArgs> TrackingChangeEventHandler;

        /// <summary>
        /// DisposedEvent
        /// </summary>
        public event EventHandler<EventArgs> DisposedEvent;

        /// <inheritdoc />
        public SQLiteConnection(string databasePath, bool storeDateTimeAsTicks = true)
            : base(databasePath, storeDateTimeAsTicks)
        {
            open = true;

            // SET BUSY TIME OUT IS 3 MINUTES
            BusyTimeout = TimeSpan.FromMinutes(3);

            InitializeSQLite();
        }

        /// <inheritdoc />
        public SQLiteConnection(string databasePath, SQLiteOpenFlags openFlags, bool storeDateTimeAsTicks = true)
            : base(databasePath, openFlags, storeDateTimeAsTicks)
        {
            open = true;

            // DEVICE LOCKED NEED THIS PERMISSION
            openFlags |= SQLiteOpenFlags.ProtectionNone;

            // SET BUSY TIME OUT IS 3 MINUTES
            BusyTimeout = TimeSpan.FromMinutes(3);

            // OPTIMIZED
            InitializeSQLite();

            // WRITE CONNECTION
            if (openFlags.HasFlag(SQLiteOpenFlags.ReadWrite))
            {
                // TRACKING CHANGED DATA
                SQLite3Extend.UpdatHook(
                    Handle,
                    (data, type, database, table, rowid) =>
                    {
                        TrackingChangeEventHandler?.Invoke(this, new HookTrackingChangeTableEventArgs
                        {
                            Type = (TrackingChangeTableType)type,
                            Table = table,
                            RowId = rowid,
                        });
                    }, null);
            }
        }

        /// <inheritdoc />
        ~SQLiteConnection()
        {
            Dispose(false);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            DisposedEvent?.Invoke(this, EventArgs.Empty);
            open = false;
            base.Dispose(disposing);
        }

        /// <summary>
        /// Config
        /// </summary>
        /// <param name="option"></param>
        public void Config(SQLite3.ConfigOption option)
        {
            SQLite3Extend.Config(Handle, option);
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            SQLite3Extend.Shutdown();
        }

        /// <inheritdoc />
        public void Initialize()
        {
            SQLite3Extend.Initialize();
        }

        /// <inheritdoc />
        public void InitializeSQLite()
        {
            lock (lockInitialize)
            {
                try
                {
                    // OPTIMIZED
                    if (!HasOptimizedConnection)
                    {
                        Shutdown();

                        // ENABLE SERIALIZED THREAD
                        Config(SQLite3.ConfigOption.Serialized);

                        // ENABLE WAL MODE
                        EnableWriteAheadLogging();

                        // ExecuteScalar<string>("PRAGMA journal_mode=WAL");
                        Initialize();

                        HasOptimizedConnection = true;
                    }
                }
                catch (SQLiteException ex)
                {
                    Console.WriteLine(ex.StackTrace);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }

        /// <inheritdoc />
        protected new virtual SQLiteCommand NewCommand()
        {
            return new SQLiteCommand(this);
        }

        /// <inheritdoc />
        protected new virtual SQLiteCommand CreateCommand(string cmdText, params object[] ps)
        {
            if (!open)
            {
                throw SQLiteException.New(SQLite3.Result.Error, "Cannot create commands from unopened database");
            }

            var cmd = NewCommand();
            cmd.CommandText = cmdText;
            foreach (var o in ps)
            {
                cmd.Bind(o);
            }

            return cmd;
        }

        /// <inheritdoc />
        protected new void Rollback()
        {
            if (open)
            {
                Rollback();
            }
        }

        /// <summary>
        /// Creates a SQLiteCommand given the command text (SQL) with arguments. Place a '?'
        /// in the command text for each of the arguments and then executes that command.
        /// It returns each row of the result using the mapping automatically generated for
        /// the given type.
        /// </summary>
        /// <param name="query">The fully escaped SQL.</param>
        /// <param name="args">
        /// </param>
        /// <returns>
        /// An enumerable with one result for each row returned by the query.
        /// </returns>
        public List<SQLiteRecord> Query(string query, params object[] args)
        {
            var cmd = CreateCommand(query, args);
            return cmd.ExecuteQuery();
        }

        /// <summary>
        /// Creates a SQLiteCommand given the command text (SQL) with arguments. Place a '?'
        /// in the command text for each of the arguments and then executes that command.
        /// It returns each row of the result using the mapping automatically generated for
        /// the given type.
        /// </summary>
        /// <param name="query">The fully escaped SQL.</param>
        /// <param name="args">
        /// Arguments to substitute for the occurrences of '?' in the query.
        /// </param>
        /// <returns>
        /// An enumerable with one result for each row returned by the query.
        /// The enumerator will call sqlite3_step on each call to MoveNext, so the database
        /// connection must remain open for the lifetime of the enumerator.
        /// </returns>
        public IEnumerable<SQLiteRecord> DeferredQuery(string query, params object[] args)
        {
            var cmd = CreateCommand(query, args);
            return cmd.ExecuteDeferredQuery();
        }
    }
}
