using SQLite;
using SQLitePCL;
using Sqlite3DatabaseHandle = SQLitePCL.sqlite3;
using Sqlite3Raw = SQLitePCL.raw;

namespace SQLiteLibrary
{
    /// <summary>
    /// SQLite3Extend
    /// </summary>
    public static class SQLite3Extend
    {
        /// <summary>
        /// UpdatHook
        /// </summary>
        /// <param name="db"></param>
        /// <param name="f"></param>
        /// <param name="v"></param>
        public static void UpdatHook(Sqlite3DatabaseHandle db, delegate_update f, object v)
        {
            Sqlite3Raw.sqlite3_update_hook(db, f, v);
        }

        /// <summary>
        /// Config
        /// </summary>
        /// <param name="db"></param>
        /// <param name="configOption"></param>
        public static void Config(Sqlite3DatabaseHandle db, SQLite3.ConfigOption configOption) =>
            Sqlite3Raw.sqlite3_config((int)configOption);

        /// <summary>
        /// Shutdown
        /// </summary>
        public static void Shutdown()
        {
            Sqlite3Raw.sqlite3_shutdown();
        }

        /// <summary>
        /// Initialize
        /// </summary>
        public static void Initialize()
        {
            Sqlite3Raw.sqlite3_initialize();
        }
    }
}
