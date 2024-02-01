using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace SQLiteLibrary
{
    /// <summary>
    /// Define SQLite record..
    /// </summary>
    public class SQLiteRecord : IReadOnlyList<object>, IReadOnlyDictionary<string, object>, IDynamicMetaObjectProvider
    {
        private readonly object[] data;
        private readonly IDictionary<string, int> columns;

        /// <summary>
        /// Initializes a new instance of the <see cref="SQLiteRecord"/> class.
        /// Construct record with number of columns
        /// </summary>
        /// <param name="columns">Column info.</param>
        /// <param name="data">Column data.</param>
        public SQLiteRecord(IDictionary<string, int> columns, object[] data = null)
        {
            this.columns = columns;
            this.data = data ?? new object[columns.Count];
        }

        /// <summary>
        /// Casting record to array.
        /// </summary>
        /// <param name="record">Record data.</param>
        /// <returns></returns>
        public static implicit operator object[](SQLiteRecord record) => record?.data;

        /// <summary>
        /// Casting record to list.
        /// </summary>
        /// <param name="record">Record data.</param>
        /// <returns></returns>
        public static implicit operator List<object>(SQLiteRecord record) => record?.data?.ToList();

        /// <summary>
        /// Casting record to dictionary.
        /// </summary>
        /// <param name="record">Record data.</param>
        /// <returns></returns>
        public static implicit operator Dictionary<string, object>(SQLiteRecord record)
        {
            if (record == null)
            {
                return null;
            }

            var result = new Dictionary<string, object>();
            foreach (var item in record)
            {
                result.Add(item.Key, item.Value);
            }

            return result;
        }

        /// <summary>
        /// Casting record to dictionary.
        /// </summary>
        /// <param name="record">Record data.</param>
        /// <returns></returns>
        public static implicit operator SQLiteRecord(Dictionary<string, object> record)
        {
            var index = 0;
            var data = new object[record.Count];
            var dic = new Dictionary<string, int>();
            foreach (var item in record)
            {
                data[index] = item.Value;
                dic[item.Key] = index;
                ++index;
            }

            return new SQLiteRecord(dic, data);
        }

        /// <inheritdoc />
        public bool ContainsKey(string key) => columns.ContainsKey(key);

        /// <inheritdoc />
        public bool TryGetValue(string key, out object value)
        {
            var result = columns.TryGetValue(key, out int index);
            value = result ? this[index] : default;
            return result;
        }

        /// <summary>
        /// Gets values base on index.
        /// </summary>
        /// <param name="column"></param>
        /// <returns></returns>
        public object this[string column]
        {
            get { return data[columns[column]]; }
            set { data[columns[column]] = value; }
        }

        /// <summary>
        /// Gets values base on index.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public object this[int index]
        {
            get { return data[index]; }
            set { data[index] = value; }
        }

        /// <inheritdoc />
        public IEnumerable<string> Keys => columns.Keys;

        /// <inheritdoc />
        public IEnumerable<object> Values => data;

        /// <inheritdoc />
        IEnumerator<object> IEnumerable<object>.GetEnumerator()
        {
            foreach (var item in data)
            {
                yield return item;
            }
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => columns.Select(column => new KeyValuePair<string, object>(column.Key, this[column.Value])).GetEnumerator();

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc />
        public int Count => data.Length;

        /// <inheritdoc />
        public DynamicMetaObject GetMetaObject(Expression expression) => new DynamicDictionaryMetaObject(expression, this);

        private class DynamicDictionaryMetaObject : DynamicMetaObject
        {
            private static readonly MethodInfo SetMethodInfo = typeof(SQLiteRecord).GetTypeInfo().GetDeclaredMethod(nameof(Set));
            private static readonly MethodInfo GetMethodInfo = typeof(SQLiteRecord).GetTypeInfo().GetDeclaredMethod(nameof(Get));
            private readonly BindingRestrictions restrictions;
            private readonly UnaryExpression expression;

            internal DynamicDictionaryMetaObject(
                Expression parameter,
                SQLiteRecord value)
                : base(parameter, BindingRestrictions.Empty, value)
            {
                // setup the binding restrictions.
                restrictions = BindingRestrictions.GetTypeRestriction(Expression, LimitType);

                // Setup the 'this' reference
                expression = Expression.Convert(Expression, LimitType);
            }

            public override DynamicMetaObject BindSetMember(SetMemberBinder binder, DynamicMetaObject value)
            {
                // setup the parameters:
                var args = new Expression[2];

                // First parameter is the name of the property to Set
                args[0] = Expression.Constant(binder.Name);

                // Second parameter is the value
                args[1] = Expression.Convert(value.Expression, typeof(object));

                // Setup the method call expression
                var methodCall = Expression.Call(expression, SetMethodInfo, args);

                var setDictionaryEntry = new DynamicMetaObject(methodCall, restrictions);

                // return that dynamic object
                return setDictionaryEntry;
            }

            public override DynamicMetaObject BindGetMember(GetMemberBinder binder)
            {
                // One parameter
                Expression[] parameters = { Expression.Constant(binder.Name) };

                // Setup the method call expression
                var methodCall = Expression.Call(expression, GetMethodInfo, parameters);

                var getDictionaryEntry = new DynamicMetaObject(methodCall, restrictions);

                // return that dynamic object
                return getDictionaryEntry;
            }
        }

        private object Set(string key, object value) => this[key] = value;

        private object Get(string key) => this[key];
    }
}
