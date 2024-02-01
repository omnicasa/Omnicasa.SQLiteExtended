using System;

namespace SQLiteLibrary.Events
{
    /// <summary>
    /// HookTrackingChangeTableEventArgs
    /// </summary>
    public class HookTrackingChangeTableEventArgs : EventArgs
    {
        /// <summary>
        /// Type
        /// </summary>
        public TrackingChangeTableType Type { get; set; }

        /// <summary>
        /// Table
        /// </summary>
        public string Table { get; set; }

        /// <summary>
        /// RowId
        /// </summary>
        public long RowId { get; set; }
    }

    /// <summary>
    /// TrackingChangeTableType
    /// </summary>
    public enum TrackingChangeTableType
    {
        Insert = 18,
        Update = 23,
        Delete = 9,
    }
}