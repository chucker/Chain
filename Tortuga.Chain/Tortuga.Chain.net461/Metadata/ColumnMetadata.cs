
using System.Data;

namespace Tortuga.Chain.Metadata
{
    /// <summary>
    /// Metadata for a table or view column
    /// </summary>
    public class ColumnMetadata
    {
        private readonly string m_ClrName;
        private readonly bool m_IsComputed;
        private readonly bool m_IsIdentity;
        private readonly bool m_IsPrimaryKey;
        private readonly string m_SqlName;
        private readonly string m_TypeName;
        private readonly SqlDbType? m_SqlDbType;

        /// <summary>
        /// Initializes a new instance of the <see cref="ColumnMetadata" /> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="isComputed">if set to <c>true</c> is a computed column.</param>
        /// <param name="isPrimaryKey">if set to <c>true</c> is a primary key.</param>
        /// <param name="isIdentity">if set to <c>true</c> [is identity].</param>
        /// <param name="typeName">Name of the type.</param>
        public ColumnMetadata(string name, bool isComputed, bool isPrimaryKey, bool isIdentity, string typeName)
        {
            m_TypeName = typeName;
            m_SqlName = name;
            m_ClrName = Utilities.ToClrName(name);
            m_IsComputed = isComputed;
            m_IsPrimaryKey = isPrimaryKey;
            m_IsIdentity = isIdentity;
            m_SqlDbType = Utilities.TypeNameToSqlDbType(typeName);
        }

        /// <summary>
        /// Gets the name used by CLR objects.
        /// </summary>
        public string ClrName
        {
            get { return m_ClrName; }
        }

        /// <summary>
        /// Gets a value indicating whether this <see cref="ColumnMetadata"/> is computed.
        /// </summary>
        /// <value>
        ///   <c>true</c> if computed; otherwise, <c>false</c>.
        /// </value>
        public bool IsComputed
        {
            get { return m_IsComputed; }
        }

        /// <summary>
        /// Gets a value indicating whether this column is an identity column.
        /// </summary>
        /// <value><c>true</c> if this instance is identity; otherwise, <c>false</c>.</value>
        public bool IsIdentity
        {
            get { return m_IsIdentity; }
        }

        /// <summary>
        /// Gets a value indicating whether this column is a primary key.
        /// </summary>
        /// <value><c>true</c> if this instance is primary key; otherwise, <c>false</c>.</value>
        public bool IsPrimaryKey
        {
            get { return m_IsPrimaryKey; }
        }

        /// <summary>
        /// Gets the name used by SQL Server, quoted.
        /// </summary>
        public string QuotedSqlName
        {
            get { return "[" + m_SqlName + "]"; }
        }

        /// <summary>
        /// Gets the name used by SQL Server.
        /// </summary>
        public string SqlName
        {
            get { return m_SqlName; }
        }
        /// <summary>
        /// Gets the column, formatted as a SQL variable.
        /// </summary>
        public string SqlVariableName
        {
            get { return "@" + m_SqlName; }
        }

        /// <summary>
        /// Gets the type used by SQL databases.
        /// </summary>
        public SqlDbType? SqlDbType
        {
            get { return m_SqlDbType; }
        }

        /// <summary>
        /// Gets the name of the type.
        /// </summary>
        /// <value>The name of the type.</value>
        public string TypeName
        {
            get { return m_TypeName; }
        }
    }
}