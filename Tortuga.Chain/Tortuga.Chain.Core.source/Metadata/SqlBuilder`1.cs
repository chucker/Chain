using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Tortuga.Anchor.Metadata;
using Tortuga.Chain.Materializers;

namespace Tortuga.Chain.Metadata
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TDbType">The type of the database type.</typeparam>
    public class SqlBuilder<TDbType>
        where TDbType : struct
    {
        private readonly SqlBuilderEntry<TDbType>[] m_Entries;

        private readonly string m_Name;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlBuilder{TDbType}"/> class.
        /// </summary>
        /// <param name="name">The name of the object (e.g. table or view).</param>
        /// <param name="columns">The columns.</param>
        internal SqlBuilder(string name, IReadOnlyList<ColumnMetadata<TDbType>> columns)
        {
            m_Name = name;
            m_Entries = new SqlBuilderEntry<TDbType>[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                m_Entries[i] = new SqlBuilderEntry<TDbType>()
                {
                    Details = column,
                    IsKey = column.IsPrimaryKey,
                    UseForInsert = !column.IsComputed && !column.IsIdentity,
                    UseForUpdate = !column.IsComputed && !column.IsIdentity
                };
            }
        }

        internal SqlBuilder(string name, IReadOnlyList<ParameterMetadata<TDbType>> parameters)
        {
            m_Name = name;
            m_Entries = new SqlBuilderEntry<TDbType>[parameters.Count];
            for (int i = 0; i < parameters.Count; i++)
            {
                var column = parameters[i];
                m_Entries[i] = new SqlBuilderEntry<TDbType>()
                {
                    Details = column,
                    IsFormalParameter = true,
                };
            }
        }

        private SqlBuilder(SqlBuilderEntry<TDbType>[] m_Entries)
        {
            this.m_Entries = m_Entries.ToArray(); //since this is an array of struct, this does a deep copy
        }

        /// <summary>
        /// Gets a value indicating whether this instance has fields marked for reading.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance has read fields; otherwise, <c>false</c>.
        /// </value>
        public bool HasReadFields
        {
            get
            {
                for (var i = 0; i < m_Entries.Length; i++)
                    if (m_Entries[i].UseForRead)
                        return true;
                return false;
            }
        }
        /// <summary>
        /// Applies an argument dictionary, overriding any perviously applied values.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="strictMode">if set to <c>true</c> [strict mode].</param>
        /// <exception cref="MappingException">This is thrown is no keys could be matched to a column. If strict mode, all keys must match columns.</exception>
        public void ApplyArgumentDictionary(IReadOnlyDictionary<string, object> value, bool strictMode)
        {
            if (value == null || value.Count == 0)
                throw new ArgumentException($"{nameof(value)} is null or empty.", nameof(value));

            bool found = false;

            foreach (var item in value)
            {
                var keyFound = false;
                for (var i = 0; i < m_Entries.Length; i++)
                {
                    if (string.Equals(m_Entries[i].Details.ClrName, item.Key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m_Entries[i].Details.SqlName, item.Key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m_Entries[i].Details.SqlVariableName, item.Key, StringComparison.OrdinalIgnoreCase)
                        )
                    {
                        if (m_Entries[i].IsFormalParameter)
                            m_Entries[i].UseParameter = true;
                        m_Entries[i].ParameterValue = item.Value ?? DBNull.Value;
                        found = true;
                        keyFound = true;
                        break;
                    }
                }
                if (strictMode && !keyFound)
                    throw new MappingException($"Strict mode was enabled, but property {item.Key} could be matched to a column in {m_Name}. Disable strict mode or remove the item from the dictionary.");
            }

            if (!found)
                throw new MappingException($"None of the keys could be matched to columns in {m_Name}.");
        }

        /// <summary>
        /// Applies the argument value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="useObjectDefinedKeys">if set to <c>true</c> use object defined keys.</param>
        /// <param name="strictMode">if set to <c>true</c> [strict mode].</param>
        /// <exception cref="ArgumentNullException">value;value is null.</exception>
        /// <exception cref="MappingException">This is thrown is no properties could be matched to a column. If strict mode, all properties must match columns.</exception>
        /// <remarks>
        /// If the object implements IReadOnlyDictionary[string, object], ApplyArgumentDictionary will be implicitly called instead.
        /// </remarks>
        public void ApplyArgumentValue(object value, bool useObjectDefinedKeys, bool strictMode)
        {
            if (value == null)
                throw new ArgumentNullException("value", "value is null.");

            if (value is IReadOnlyDictionary<string, object>)
            {
                ApplyArgumentDictionary((IReadOnlyDictionary<string, object>)value, strictMode);
                return;
            }

            if (useObjectDefinedKeys)
                for (var i = 0; i < m_Entries.Length; i++)
                    m_Entries[i].IsKey = false;

            var found = false;

            var metadata = MetadataCache.GetMetadata(value.GetType());
            foreach (var property in metadata.Properties)
            {
                var propertyFound = false;

                if (property.MappedColumnName == null)
                    continue;

                for (var i = 0; i < m_Entries.Length; i++)
                {
                    if (m_Entries[i].Details.ClrName.Equals(property.MappedColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        propertyFound = true;
                        if (useObjectDefinedKeys && property.IsKey)
                            m_Entries[i].IsKey = true;
                        m_Entries[i].ParameterValue = property.InvokeGet(value) ?? DBNull.Value;
                        if (property.IgnoreOnInsert)
                            m_Entries[i].UseForInsert = false;
                        if (property.IgnoreOnUpdate)
                            m_Entries[i].UseForUpdate = false;
                        if (m_Entries[i].IsFormalParameter)
                            m_Entries[i].UseParameter = true;
                        break;
                    }
                }
                if (strictMode && !propertyFound)
                    throw new MappingException($"Strict mode was enabled, but property {property.Name} could be matched to a column in {m_Name}. Disable strict mode or mark the property as NotMapped.");
            }

            if (!found)
                throw new MappingException($"None of the properties on {value.GetType().Name} could be matched to columns in {m_Name}.");
        }

        /// <summary>
        /// Uses a desired columns enumeration to indicate which columns should be set to read-mode.
        /// </summary>
        /// <param name="desiredColumns">The desired columns. This also supports Materializer.NoColumns, Materializer.AutoSelectDesiredColumns, and Materializer.AllColumns.</param>
        /// <param name="strictMode">if set to <c>true</c> [strict mode].</param>
        /// <exception cref="MappingException">This is thrown is no desired columns were actually part of the table or view. If strict mode, all desired columns must be found.</exception>        
        /// <remarks>Calling this a second time will be additive with prior call.</remarks>
        public void ApplyDesiredColumns(IEnumerable<string> desiredColumns, bool strictMode)
        {
            if (desiredColumns == null)
                throw new ArgumentNullException(nameof(desiredColumns), $"{nameof(desiredColumns)} is null.");

            bool found = false;

            if (desiredColumns == Materializer.NoColumns)
                return;//no-op, we default m_Entries[i].Read to false

            if (desiredColumns == Materializer.AutoSelectDesiredColumns)
            {
                //we want primary keys
                for (var i = 0; i < m_Entries.Length; i++)
                {
                    if (m_Entries[i].IsKey)
                    {
                        m_Entries[i].UseForRead = true;
                        found = true;
                    }
                }
                if (found)
                    return;

                //we will accept identity columns
                {
                    for (var i = 0; i < m_Entries.Length; i++)
                    {
                        if (m_Entries[i].Details.IsIdentity)
                        {
                            m_Entries[i].UseForRead = true;
                            found = true;
                        }
                    }
                }
                if (found)
                    return;

                //we have nothing
                if (!found)
                    throw new MappingException($"Could not find a primary key for {m_Name}.");
            }

            if (desiredColumns == Materializer.AllColumns)
            {
                for (var i = 0; i < m_Entries.Length; i++)
                    m_Entries[i].UseForRead = true;
                return;
            }

            foreach (var column in desiredColumns)
            {
                var columnFound = false;

                for (var i = 0; i < m_Entries.Length; i++)
                {

                    if (string.Equals(m_Entries[i].Details.ClrName, column, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m_Entries[i].Details.SqlName, column, StringComparison.OrdinalIgnoreCase))
                    {
                        m_Entries[i].UseForRead = true;
                        columnFound = true;
                        found = true;
                        break;
                    }

                }

                if (strictMode && !columnFound)
                    throw new MappingException($"Strict mode was enabled, but desired column {column} was not found on {m_Name}. Disable strict mode or mark the property as NotMapped.");
            }

            if (!found)
                throw new MappingException($"None of the desired columns were found on {m_Name}.");
        }


        /// <summary>
        /// Applies the filter value, returning a set of expressions suitable for use in a WHERE clause.
        /// </summary>
        /// <param name="filterValue">The filter value.</param>
        /// <param name="strictMode">if set to <c>true</c> [strict mode].</param>
        /// <returns></returns>
        public string ApplyFilterValue(object filterValue, bool strictMode)
        {
            if (filterValue == null)
                throw new ArgumentNullException(nameof(filterValue), $"{nameof(filterValue)} is null.");

            var parts = new List<string>();
            bool found = false;

            if (filterValue is IReadOnlyDictionary<string, object>)
            {
                foreach (var item in (IReadOnlyDictionary<string, object>)filterValue)
                {
                    var keyFound = false;
                    for (var i = 0; i < m_Entries.Length; i++)
                    {
                        if (string.Equals(m_Entries[i].Details.ClrName, item.Key, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(m_Entries[i].Details.SqlName, item.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            var value = item.Value ?? DBNull.Value;

                            if (m_Entries[i].ParameterValue == DBNull.Value)
                            {
                                parts.Add($"{m_Entries[i].Details.QuotedSqlName} IS NULL");
                            }
                            else
                            {
                                m_Entries[i].ParameterValue = value;
                                m_Entries[i].UseParameter = true;
                                parts.Add($"{m_Entries[i].Details.QuotedSqlName} = {m_Entries[i].Details.SqlVariableName}");
                            }

                            found = true;
                            keyFound = true;
                            break;
                        }
                    }
                    if (strictMode && !keyFound)
                        throw new MappingException($"Strict mode was enabled, but property {item.Key} could be matched to a column in {m_Name}. Disable strict mode or remove the item from the dictionary.");
                }
            }
            else
            {
                foreach (var property in MetadataCache.GetMetadata(filterValue.GetType()).Properties.Where(p => p.MappedColumnName != null))
                {
                    var propertyFound = false;
                    for (var i = 0; i < m_Entries.Length; i++)
                    {
                        if (m_Entries[i].Details.ClrName.Equals(property.MappedColumnName, StringComparison.OrdinalIgnoreCase))
                        {
                            var value = property.InvokeGet(filterValue) ?? DBNull.Value;

                            if (m_Entries[i].ParameterValue == DBNull.Value)
                            {
                                parts.Add($"{m_Entries[i].Details.QuotedSqlName} IS NULL");
                            }
                            else
                            {
                                m_Entries[i].ParameterValue = value;
                                m_Entries[i].UseParameter = true;
                                parts.Add($"{m_Entries[i].Details.QuotedSqlName} = {m_Entries[i].Details.SqlVariableName}");
                            }

                            found = true;
                            propertyFound = true;
                            break;
                        }
                    }
                    if (strictMode && !propertyFound)
                        throw new MappingException($"Strict mode was enabled, but property {property.Name} could be matched to a column in {m_Name}. Disable strict mode or mark the property as NotMapped.");

                }

            }

            if (!found)
                throw new MappingException($"None of the properties on {filterValue.GetType().Name} could be matched to columns in {m_Name}.");

            return string.Join(" AND ", parts);
        }

        /// <summary>
        /// Overrides the pervious selected values with the values in the indicated object.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="strictMode">if set to <c>true</c> [strict mode].</param>
        /// <exception cref="ArgumentNullException">value;value is null.</exception>
        /// <exception cref="MappingException">This is thrown is no properties could be matched to a column. If strict mode, all properties must match columns.</exception>
        /// <remarks>This will not alter the IsPrimaryKey, Insert, or Update column settings.
        /// If the object implements IReadOnlyDictionary[string, object], ApplyArgumentDictionary will be implicitly called instead.
        /// </remarks>
        public void ApplyValueOverrides(object value, bool strictMode)
        {
            if (value == null)
                throw new ArgumentNullException("value", "value is null.");

            if (value is IReadOnlyDictionary<string, object>)
            {
                ApplyArgumentDictionary((IReadOnlyDictionary<string, object>)value, strictMode);
                return;
            }

            var found = false;

            var metadata = MetadataCache.GetMetadata(value.GetType());
            foreach (var property in metadata.Properties)
            {
                var propertyFound = false;

                if (property.MappedColumnName == null)
                    continue;

                for (var i = 0; i < m_Entries.Length; i++)
                {
                    if (m_Entries[i].Details.ClrName.Equals(property.MappedColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        propertyFound = true;
                        if (m_Entries[i].IsFormalParameter)
                            m_Entries[i].UseParameter = true;
                        m_Entries[i].ParameterValue = property.InvokeGet(value) ?? DBNull.Value;
                        break;
                    }
                }
                if (strictMode && !propertyFound)
                    throw new MappingException($"Strict mode was enabled, but property {property.Name} could be matched to a column in {m_Name}. Disable strict mode or mark the property as NotMapped.");
            }

            if (!found)
                throw new MappingException($"None of the properties on {value.GetType().Name} could be matched to columns in {m_Name}.");
        }
        /// <summary>
        /// Builds a complete select statement using the keys as a filter. This is mostly used in conjunction with an UPDATE or DELETE operation.
        /// </summary>
        /// <param name="sql">The SQL.</param>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="footer">Optional footer, usually the statement terminator (;).</param>
        public void BuildDeleteStatement(StringBuilder sql, string tableName, string footer)
        {

            if (sql == null)
                throw new ArgumentNullException(nameof(sql), $"{nameof(sql)} is null.");
            if (string.IsNullOrEmpty(tableName))
                throw new ArgumentException($"{nameof(tableName)} is null or empty.", nameof(tableName));

            sql.Append("DELETE FROM " + tableName);
            BuildWhereClause(sql, " WHERE ", footer);
        }

        /// <summary>
        /// Builds a list of columns suitable for using in an INSERT statement.
        /// This will mark key columns for use in parameter building.
        /// </summary>
        /// <param name="sql">The SQL being generated.</param>
        /// <param name="header">The optional header. e.g. "INSERT tableName (".</param>
        /// <param name="prefix">An optional prefix for each column name.</param>
        /// <param name="footer">The optional footer. Usually just ")".</param>
        public void BuildInsertClause(StringBuilder sql, string header, string prefix, string footer)
        {
            if (sql == null)
                throw new ArgumentNullException(nameof(sql), $"{nameof(sql)} was null.");

            sql.Append(header);
            sql.Append(string.Join(", ", GetInsertColumns().ToList().Select(x => prefix + x.QuotedSqlName)));
            sql.Append(footer);
        }

        /// <summary>
        /// Builds a complete insert statement.
        /// </summary>
        /// <param name="sql">The SQL.</param>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="footer">The footer.</param>
        public void BuildInsertStatement(StringBuilder sql, string tableName, string footer)
        {
            if (sql == null)
                throw new ArgumentNullException(nameof(sql), $"{nameof(sql)} is null.");
            if (string.IsNullOrEmpty(tableName))
                throw new ArgumentException($"{nameof(tableName)} is null or empty.", nameof(tableName));

            BuildInsertClause(sql, "INSERT INTO " + tableName + " (", null, ")");
            BuildValuesClause(sql, " VALUES (", ")");
            sql.Append(footer);
        }

        /// <summary>
        /// Builds a complete select statment using the keys as a filter. This is mostly used in conjunction with an UPDATE or DELETE operation.
        /// </summary>
        /// <param name="sql">The SQL.</param>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="footer">Optional footer, usually the statement terminator (;).</param>
        public void BuildSelectByKeyStatement(StringBuilder sql, string tableName, string footer)
        {
            if (sql == null)
                throw new ArgumentNullException(nameof(sql), $"{nameof(sql)} is null.");
            if (string.IsNullOrEmpty(tableName))
                throw new ArgumentException($"{nameof(tableName)} is null or empty.", nameof(tableName));

            if (!HasReadFields)
                return;

            BuildSelectClause(sql, "SELECT ", null, " FROM " + tableName);
            BuildWhereClause(sql, " WHERE ", footer);
        }

        /// <summary>
        /// Builds a list of columns suitable for using in a SELECT or OUTPUT statement.
        /// </summary>
        /// <param name="sql">The SQL being generated.</param>
        /// <param name="header">The optional header (e.g. "SELECT, OUTPUT).</param>
        /// <param name="prefix">An optional prefix for each column name.</param>
        /// <param name="footer">The optional footer.</param>
        /// <remarks>
        /// If no columns are marked for reading, the header and footer won't be emitted.
        /// </remarks>
        public void BuildSelectClause(StringBuilder sql, string header, string prefix, string footer)
        {
            if (sql == null)
                throw new ArgumentNullException(nameof(sql), $"{nameof(sql)} was null.");

            if (!HasReadFields)
                return;

            sql.Append(header);
            sql.Append(string.Join(", ", GetSelectColumns().Select(x => prefix + x)));
            sql.Append(footer);
        }

        /// <summary>
        /// Builds a list of assignments suitable for using in the SET clause of UPDATE statement. This does not include the actual SET keyword.
        /// This will mark key columns for use in parameter building.
        /// </summary>
        /// <param name="sql">The SQL being generated.</param>
        /// <param name="header">The optional header. Usually not used.</param>
        /// <param name="prefix">An optional prefix for each column name.</param>
        /// <param name="footer">The optional footer. Usually not used.</param>
        public void BuildSetClause(StringBuilder sql, string header, string prefix, string footer)
        {
            if (sql == null)
                throw new ArgumentNullException(nameof(sql), $"{nameof(sql)} was null.");

            sql.Append(header);
            sql.Append(string.Join(", ", GetUpdateColumns().Select(x => $"{prefix}{x.QuotedSqlName} = {x.SqlVariableName}")));
            sql.Append(footer);
        }

        /// <summary>
        /// Builds the complete update statment using primary keys.
        /// </summary>
        /// <param name="sql">The SQL.</param>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="footer">The footer.</param>
        public void BuildUpdateByKeyStatement(StringBuilder sql, string tableName, string footer)
        {
            BuildSetClause(sql, "UPDATE " + tableName + " SET ", null, null);
            BuildWhereClause(sql, " WHERE ", null);
            sql.Append(footer);
        }

        /// <summary>
        /// Builds a list of columns suitable for using in the VALUES clause of INSERT statement. This does not include the actual VALUES keyword.
        /// This will mark key columns for use in parameter building.
        /// </summary>
        /// <param name="sql">The SQL being generated.</param>
        /// <param name="header">The optional header. Usually "VALUES  (".</param>
        /// <param name="footer">The optional footer. Usually just ")".</param>
        public void BuildValuesClause(StringBuilder sql, string header, string footer)
        {
            if (sql == null)
                throw new ArgumentNullException(nameof(sql), $"{nameof(sql)} was null.");

            sql.Append(header);
            sql.Append(string.Join(", ", GetInsertColumns().Select(x => x.SqlVariableName)));
            sql.Append(footer);
        }

        /// <summary>
        /// Builds the standard WHERE clause from Key columns.
        /// This will mark key columns for use in parameter building.
        /// </summary>
        /// <param name="sql">The SQL being generated.</param>
        /// <param name="header">The optional header Usually "WHERE".</param>
        /// <param name="footer">The optional footer. Usually not used.</param>
        public void BuildWhereClause(StringBuilder sql, string header, string footer)
        {
            if (sql == null)
                throw new ArgumentNullException(nameof(sql), $"{nameof(sql)} was null.");

            sql.Append(header);
            sql.Append(string.Join(" AND ", GetKeyColumns().Select(x => x.QuotedSqlName + " = " + x.SqlVariableName)));
            sql.Append(footer);
        }
        /// <summary>
        /// Clones this instance so that you can modify it without affecting the cached origianl.
        /// </summary>
        /// <returns></returns>
        public SqlBuilder<TDbType> Clone()
        {
            return new SqlBuilder<TDbType>(m_Entries);
        }

        /// <summary>
        /// Gets the insert columns.
        /// </summary>
        /// <returns>Each pair has the column's QuotedSqlName and SqlVariableName</returns>
        /// <remarks>This will mark the returned columns as participating in the parameter generation.</remarks>
        [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public IEnumerable<ColumnNamePair> GetInsertColumns()
        {
            for (var i = 0; i < m_Entries.Length; i++)
            {
                if (m_Entries[i].UseForInsert && m_Entries[i].ParameterValue != null)
                {
                    m_Entries[i].UseParameter = true;
                    yield return new ColumnNamePair(m_Entries[i].Details.QuotedSqlName, m_Entries[i].Details.SqlVariableName);
                }
            }
        }

        /// <summary>
        /// Gets the key columns.
        /// </summary>
        /// <returns>Each pair has the column's QuotedSqlName and SqlVariableName</returns>
        /// <remarks>This will mark the returned columns as participating in the parameter generation.</remarks>
        [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public IEnumerable<ColumnNamePair> GetKeyColumns()
        {
            for (var i = 0; i < m_Entries.Length; i++)
            {
                if (m_Entries[i].IsKey)
                {
                    m_Entries[i].UseParameter = true;
                    yield return new ColumnNamePair(m_Entries[i].Details.QuotedSqlName, m_Entries[i].Details.SqlVariableName);
                }
            }
        }

        /// <summary>
        /// Gets every column with a ParameterValue.
        /// </summary>
        /// <returns>Each pair has the column's QuotedSqlName and SqlVariableName</returns>
        /// <remarks>This will mark the returned columns as participating in the parameter generation.</remarks>
        [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public IEnumerable<ColumnNamePair> GetParameterizedColumns()
        {
            for (var i = 0; i < m_Entries.Length; i++)
            {
                if (m_Entries[i].ParameterValue != null)
                {
                    m_Entries[i].UseParameter = true;
                    yield return new ColumnNamePair(m_Entries[i].Details.QuotedSqlName, m_Entries[i].Details.SqlVariableName);
                }
            }
        }

        /// <summary>
        /// Gets the parameters.
        /// </summary>
        /// <typeparam name="TParameter">The type of the parameter.</typeparam>
        /// <param name="parameterBuilder">The parameter builder. This should set the parameter's database specific DbType property.</param>
        /// <returns></returns>
        public List<TParameter> GetParameters<TParameter>(Func<TDbType?, TParameter> parameterBuilder)
            where TParameter : DbParameter
        {
            if (parameterBuilder == null)
                throw new ArgumentNullException(nameof(parameterBuilder), $"{nameof(parameterBuilder)} is null.");

            var result = new List<TParameter>();

            for (var i = 0; i < m_Entries.Length; i++)
            {
                if (m_Entries[i].UseParameter && m_Entries[i].ParameterValue != null)
                {
                    var item = parameterBuilder(m_Entries[i].Details.DbType);
                    item.ParameterName = m_Entries[i].Details.SqlVariableName;
                    item.Value = m_Entries[i].ParameterValue;
                    result.Add(item);
                }
            }

            return result;
        }


        /// <summary>
        /// Gets the select columns.
        /// </summary>
        /// <returns>Each entry has the column's QuotedSqlName</returns>
        [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public IEnumerable<string> GetSelectColumns()
        {
            for (var i = 0; i < m_Entries.Length; i++)
            {
                if (m_Entries[i].UseForRead)
                    yield return m_Entries[i].Details.QuotedSqlName;
            }
        }

        /// <summary>
        /// Gets the update columns.
        /// </summary>
        /// <returns>Each pair has the column's QuotedSqlName and SqlVariableName</returns>
        /// <remarks>This will mark the returned columns as participating in the parameter generation.</remarks>
        [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public IEnumerable<ColumnNamePair> GetUpdateColumns()
        {
            for (var i = 0; i < m_Entries.Length; i++)
            {
                if (m_Entries[i].UseForUpdate && m_Entries[i].ParameterValue != null)
                {
                    m_Entries[i].UseParameter = true;
                    yield return new ColumnNamePair(m_Entries[i].Details.QuotedSqlName, m_Entries[i].Details.SqlVariableName);
                }
            }
        }


    }


}