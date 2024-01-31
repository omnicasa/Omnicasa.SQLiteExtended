using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using SQLite;
using Sqlite3Statement = SQLitePCL.sqlite3_stmt;

namespace Omnicasa.SQLiteExtended
{
    /// <inheritdoc/>
    internal static class EnumCache
    {
        private static readonly Dictionary<Type, EnumCacheInfo> Cache = new ();

        /// <inheritdoc/>
        public static EnumCacheInfo GetInfo(Type type)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(type, out var info))
                {
                    return info;
                }

                info = new EnumCacheInfo(type);
                Cache[type] = info;

                return info;
            }
        }
    }

    /// <summary>SQLite command.</summary>
    public class SQLiteCommand
    {
        private const string DateTimeExactStoreFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff";

        private static readonly IntPtr NegativePointer = new (-1);

        private readonly SQLiteConnection sqliteConnection;
        private readonly List<Binding> bindings;

        /// <summary>
        /// Initializes a new instance of the <see cref="SQLiteCommand"/> class.
        /// Constructor.
        /// </summary>
        /// <param name="conn"></param>
        public SQLiteCommand(SQLiteConnection conn)
        {
            sqliteConnection = conn;
            if (sqliteConnection?.IsOpen != true)
            {
                throw SQLiteException.New(SQLite3.Result.Error, "Cannot create commands from unopened database");
            }

            bindings = new List<Binding>();
            CommandText = string.Empty;
        }

        /// <summary>
        /// Gets or sets command text.
        /// </summary>
        public string CommandText { get; set; }

        private Sqlite3Statement Prepare()
        {
            try
            {
                var stmt = SQLite3.Prepare2(sqliteConnection.Handle, CommandText);
                BindAll(stmt);

                return stmt;
            }
            catch (SQLiteException e)
            {
                Debug.WriteLine(CommandText);
                Debug.WriteLine(e);
                var innerException = SQLiteException.New(SQLite3.Result.Internal, CommandText);
                throw innerException;
            }
            catch (Exception e)
            {
                Debug.WriteLine(CommandText);
                Debug.WriteLine(e);
                throw new Exception(CommandText, e);
            }
        }

        /// <summary>
        /// Binding.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="val"></param>
        public void Bind(string name, object val)
        {
            bindings.Add(new Binding
            {
                Name = name,
                Value = val,
            });
        }

        /// <summary>
        /// Binding.
        /// </summary>
        /// <param name="val"></param>
        public void Bind(object val)
        {
            Bind(null, val);
        }

        /// <summary>
        /// Binding.
        /// </summary>
        /// <param name="stmt"></param>
        public void BindAll(Sqlite3Statement stmt)
        {
            int nextIdx = 1;
            foreach (var b in bindings)
            {
                b.Index = b.Name != null ? SQLite3.BindParameterIndex(stmt, b.Name) : nextIdx++;

                BindParameter(stmt, b.Index, b.Value, sqliteConnection.StoreDateTimeAsTicks);

                if (sqliteConnection.Trace)
                {
                    Debug.WriteLine($"BindParameter: {b.Index}= {b.Value}");
                }
            }
        }

        /// <summary>
        /// BindParameter
        /// </summary>
        /// <param name="stmt"></param>
        /// <param name="index"></param>
        /// <param name="value"></param>
        /// <param name="storeDateTimeAsTicks"></param>
        /// <exception cref="NotSupportedException"></exception>
        internal static void BindParameter(Sqlite3Statement stmt, int index, object value, bool storeDateTimeAsTicks)
        {
            if (value == null)
            {
                SQLite3.BindNull(stmt, index);
            }
            else
            {
#pragma warning disable SA1121
                if (value is int i)
                {
                    SQLite3.BindInt(stmt, index, i);
                }
                else if (value is String)
                {
                    SQLite3.BindText(stmt, index, (string)value, -1, NegativePointer);
                }
                else if (value is Byte || value is UInt16 || value is SByte || value is Int16)
                {
                    SQLite3.BindInt(stmt, index, Convert.ToInt32(value));
                }
                else if (value is Boolean)
                {
                    SQLite3.BindInt(stmt, index, (bool)value ? 1 : 0);
                }
                else if (value is UInt32 || value is Int64)
                {
                    SQLite3.BindInt64(stmt, index, Convert.ToInt64(value));
                }
                else if (value is Single || value is Double || value is Decimal)
                {
                    SQLite3.BindDouble(stmt, index, Convert.ToDouble(value));
                }
                else if (value is TimeSpan span)
                {
                    SQLite3.BindInt64(stmt, index, span.Ticks);
                }
                else if (value is DateTime time)
                {
                    if (storeDateTimeAsTicks)
                    {
                        SQLite3.BindInt64(stmt, index, time.Ticks);
                    }
                    else
                    {
                        SQLite3.BindText(stmt, index, time.ToString(DateTimeExactStoreFormat, System.Globalization.CultureInfo.InvariantCulture), -1, NegativePointer);
                    }
                }
                else if (value is DateTimeOffset offset)
                {
                    SQLite3.BindInt64(stmt, index, offset.UtcTicks);
                }
                else if (value is byte[] v)
                {
                    SQLite3.BindBlob(stmt, index, v, v.Length, NegativePointer);
                }
                else if (value is Guid guid)
                {
                    SQLite3.BindText(stmt, index, guid.ToString(), 72, NegativePointer);
                }
                else
                {
                    // Now we could possibly get an enum, retrieve cached info
                    var valueType = value.GetType();
                    var enumInfo = EnumCache.GetInfo(valueType);
                    if (enumInfo.IsEnum)
                    {
                        var enumIntValue = Convert.ToInt32(value);
                        if (enumInfo.StoreAsText)
                        {
                            SQLite3.BindText(stmt, index, enumInfo.EnumValues[enumIntValue], -1, NegativePointer);
                        }
                        else
                        {
                            SQLite3.BindInt(stmt, index, enumIntValue);
                        }
                    }
                    else
                    {
                        throw new NotSupportedException("Cannot store type: " + Orm.GetType(value));
                    }
                }
#pragma warning restore SA1121
            }
        }

        /// <summary>
        /// Execute non query.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="SQLiteException"></exception>
        /// <exception cref="NotNullConstraintViolationException"></exception>
        public int ExecuteNonQuery()
        {
            if (sqliteConnection.Trace)
            {
                Debug.WriteLine("Executing: " + CommandText);
            }

            using var stmt = Prepare();
            var r = SQLite3.Step(stmt);
            SQLite3.Finalize(stmt);
            switch (r)
            {
                case SQLite3.Result.Done:
                    int rowsAffected = SQLite3.Changes(sqliteConnection.Handle);
                    return rowsAffected;
                case SQLite3.Result.Error:
                    string msg = SQLite3.GetErrmsg(sqliteConnection.Handle);
                    throw SQLiteException.New(r, msg);
                case SQLite3.Result.Constraint:
                    if (SQLite3.ExtendedErrCode(sqliteConnection.Handle) == SQLite3.ExtendedResult.ConstraintNotNull)
                    {
                        throw NotNullConstraintViolationException.New(r, SQLite3.GetErrmsg(sqliteConnection.Handle));
                    }

                    break;
            }

            throw SQLiteException.New(r, r.ToString());
        }

        /// <summary>
        /// Execute query.
        /// </summary>
        /// <returns></returns>
        public List<SQLiteRecord> ExecuteQuery()
        {
            var result = ExecuteDeferredQuery().ToList();
            return result;
        }

        private class Enumerable : IEnumerable<SQLiteRecord>
        {
            private readonly SQLiteCommand sqliteCommand;

            public Enumerable(SQLiteCommand command)
            {
                sqliteCommand = command;
            }

            public IEnumerator<SQLiteRecord> GetEnumerator()
            {
                return new Enumerator(sqliteCommand.Prepare);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private class ColumnNameComparer : IEqualityComparer<string>
            {
                public bool Equals(string x, string y)
                {
                    return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
                }

                public int GetHashCode(string obj)
                {
                    return obj.ToLower().GetHashCode();
                }
            }

            private class Enumerator : IEnumerator<SQLiteRecord>
            {
                private static readonly ColumnNameComparer comparer = new ();

                private readonly Func<Sqlite3Statement> prepare;
                private readonly IDictionary<string, int> columnInfo;
                private Sqlite3Statement statement;

                public Enumerator(Func<Sqlite3Statement> prepare)
                {
                    this.prepare = prepare;
                    statement = this.prepare();
                    var count = SQLite3.ColumnCount(statement);
                    columnInfo = new Dictionary<string, int>(count, comparer);
                    var dict = new Dictionary<string, int>();
                    for (var index = 0; index < count; ++index)
                    {
                        var name = SQLite3.ColumnName(statement, index);
                        if (columnInfo.ContainsKey(name))
                        {
                            if (dict.ContainsKey(name))
                            {
                                dict[name] = dict[name] + 1;
                                name = name + dict[name];
                            }
                            else
                            {
                                dict[name] = 1;
                                name = name + 1;
                            }

                            columnInfo.Add(name, index);
                        }
                        else
                        {
                            columnInfo.Add(name, index);
                        }
                    }
                }

                public bool MoveNext()
                {
                    return SQLite3.Step(statement) == SQLite3.Result.Row;
                }

                public void Reset()
                {
                    Dispose();
                    statement = prepare();
                }

                object IEnumerator.Current => Current;

                public SQLiteRecord Current
                {
                    get
                    {
                        var result = new SQLiteRecord(columnInfo);
                        for (var i = 0; i < columnInfo.Count; i++)
                        {
                            var colType = SQLite3.ColumnType(statement, i);
                            result[i] = colType switch
                            {
                                SQLite3.ColType.Integer => SQLite3.ColumnInt64(statement, i),
                                SQLite3.ColType.Float => SQLite3.ColumnDouble(statement, i),
                                SQLite3.ColType.Text => SQLite3.ColumnText(statement, i),
                                SQLite3.ColType.Blob => SQLite3.ColumnBlob(statement, i),
                                SQLite3.ColType.Null => SQLite3.ColumnBlob(statement, i),
                                _ => throw new ArgumentOutOfRangeException(),
                            };
                        }

                        return result;
                    }
                }

                public void Dispose()
                {
                    SQLite3.Finalize(statement);
                    statement.Dispose();
                }
            }
        }

        /// <summary>
        /// Execute deferred query.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<SQLiteRecord> ExecuteDeferredQuery()
        {
            return new Enumerable(this);
        }
    }

    /// <inheritdoc/>
    internal class Binding
    {
        /// <inheritdoc/>
        public string Name { get; set; }

        /// <inheritdoc/>
        public object Value { get; set; }

        /// <inheritdoc/>
        public int Index { get; set; }
    }

    /// <inheritdoc/>
    internal class EnumCacheInfo
    {
        /// <inheritdoc/>
        public bool IsEnum { get; }

        /// <inheritdoc/>
        public bool StoreAsText { get; }

        /// <inheritdoc/>
        public Dictionary<int, string> EnumValues { get; }

        /// <inheritdoc/>
        public EnumCacheInfo(Type type)
        {
            var typeInfo = type.GetTypeInfo();

            IsEnum = typeInfo.IsEnum;

            if (!IsEnum)
            {
                return;
            }

            StoreAsText = typeInfo.CustomAttributes.Any(x => x.AttributeType == typeof(StoreAsTextAttribute));

            EnumValues = StoreAsText ? Enum.GetValues(type).Cast<object>().ToDictionary(Convert.ToInt32, x => x.ToString()) : Enum.GetValues(type).Cast<object>().ToDictionary(Convert.ToInt32, x => Convert.ToInt32(x).ToString());
        }
    }
}
