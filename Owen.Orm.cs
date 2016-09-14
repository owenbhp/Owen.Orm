//Owen.Orm - A very simple .NET ORM in C#
//
//MIT License
//Copyright (c) 2016 Owen P. Amador
//
//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.

// Tested on SQL Server, MySql/MariaDB, PostgreSQL, and SQLite (System.Data.SQLite)


using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;

namespace Owen.Orm
{
    public class Sql : Attribute
    {
        public string Schema;
        public string Table;
        public string Column;
        public string IdentifierCase; //"lower" or "upper"
    }

    public abstract class DomainObject
    {
    }


    public class Query<TDbConnection, TDomainObject> where TDbConnection : DbConnection, new() where TDomainObject : DomainObject, new()
    {
        protected delegate TDomainObject DomainObjectConverter(DbDataReader reader);
        protected class SqlParameter
        {
            public string Name;
            public object Value;
        }
        private readonly string _sqlCommand;
        protected readonly string _domainObjectName = typeof(TDomainObject).Name;
        protected readonly PropertyInfo[] _domainObjectProperties = typeof(TDomainObject).GetProperties();
        protected DomainObjectConverter ToDomainObject;
        public string ConnectionString { get; set; }

        public Query(string connectionString, string sqlCommand)
        {
            ConnectionString = connectionString;
            _sqlCommand = sqlCommand;
        }

        public virtual List<TDomainObject> ToList()
        {
            return Execute(true, null) as List<TDomainObject>;
        }

        public virtual DataTable ToDataTable()
        {
            return Execute(false, null) as DataTable;
        }

        protected object Execute(bool isList, string sqlCommand, params SqlParameter[] sqlParameters)
        {
            object dataToReturn;
            using (var connection = new TDbConnection())
            {
                connection.ConnectionString = ConnectionString;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sqlCommand ?? _sqlCommand;
                    foreach (var parameter in sqlParameters)
                    {
                        DbParameter dbparameter = command.CreateParameter();
                        dbparameter.ParameterName = parameter.Name;
                        dbparameter.Value = parameter.Value;
                        command.Parameters.Add(dbparameter);
                    }
                    try
                    {
                        connection.Open();
                        var dataReader = command.ExecuteReader();
                        if (isList)
                        {
                            var list = new List<TDomainObject>();
                            if (ToDomainObject == null)
                                while (dataReader.Read())
                                {
                                    int index = 0;
                                    var domainObject = new TDomainObject();
                                    foreach (var property in _domainObjectProperties)
                                    {
                                        property.SetValue(domainObject, dataReader[index] == DBNull.Value ? null : Convert.ChangeType(dataReader[index], Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType), null);
                                        index++;
                                    }
                                    list.Add(domainObject);
                                }
                            else
                                while (dataReader.Read()) list.Add(ToDomainObject(dataReader));
                            dataToReturn = list;
                        }
                        else
                        {
                            var dataTable = new DataTable();
                            dataTable.TableName = _domainObjectName;
                            dataTable.Load(dataReader);
                            dataToReturn = dataTable;
                        }
                        dataReader.Close();
                        return dataToReturn;
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Erroneous SQL Statement or Mapping Error (The query result columns do not match the Domain Object's properties[count, type, order]", ex);
                    }
                }
            }
        }
    }

    public class View<TDbConnection, TDomainObject> : Query<TDbConnection, TDomainObject> where TDbConnection : DbConnection, new() where TDomainObject : DomainObject, new()
    {
        protected readonly string _identifierCase;
        protected readonly string _sqlFullyQualifiedTableName;
        protected readonly string[] _sqlColumnAliases;
        protected readonly string _sqlSelectCommand;
        protected readonly string _sqlColumnNames;

        public View(string connectionString) : base(connectionString, null)
        {
            _sqlFullyQualifiedTableName = GetFullyQualifiedTableName(out _identifierCase);
            _sqlColumnAliases = GetColumnAliases();
            _sqlSelectCommand = CreateSelectCommandString(out _sqlColumnNames);
        }

        public override DataTable ToDataTable()
        {
            return Read(false, null) as DataTable;
        }

        public override List<TDomainObject> ToList()
        {
            return Read(true, null) as List<TDomainObject>;
        }

        public DataTable ToDataTable(string customSelectSqlString, params object[] sqlParameters)
        {
            return Read(false, customSelectSqlString, sqlParameters) as DataTable;
        }

        public List<TDomainObject> ToList(string customSelectSqlString, params object[] sqlParameters)
        {
            return Read(true, customSelectSqlString, sqlParameters) as List<TDomainObject>;
        }

        protected object Read(bool isList, string customSelectSqlString, params object[] sqlParameters)
        {
            try
            {
                if (customSelectSqlString != null)
                {
                    string commandText = customSelectSqlString.Replace("{this}", " " + _sqlColumnNames + " FROM " + _sqlFullyQualifiedTableName);
                    int parametercount = sqlParameters.Length;
                    if (parametercount > 0)
                    {
                        for (int i = parametercount - 1; i >= 0; i--)
                            commandText = commandText.Replace("{" + i + "}", "@" + _domainObjectName + "Parameter" + i);
                        SqlParameter[] parameters = new SqlParameter[parametercount];
                        for (int i = 0; i < parametercount; i++)
                            parameters[i] = new SqlParameter { Name = "@" + _domainObjectName + "Parameter" + i, Value = sqlParameters[i] };
                        return Execute(isList, commandText, parameters);
                    }
                    else
                        return Execute(isList, commandText);
                }
                else
                    return Execute(isList, _sqlSelectCommand);
            }
            catch (Exception ex)
            {
                throw new ApplicationException(string.Format("There was an error reading data from {0} View/Table. Make sure that the columns of the View/Table match the Domain Object's properties [name, type, order, number]", _domainObjectName), ex);
            }
        }

        // HELPER METHODS
        protected string[] GetColumnAliases()
        {
            string[] domainObjectPropertyNames = new string[_domainObjectProperties.Length];
            for (int i = 0; i < _domainObjectProperties.Length; i++)
            {
                string column = null;
                object[] attributes = _domainObjectProperties[i].GetCustomAttributes(false);
                foreach (object attribute in attributes)
                {
                    if (attribute is Sql) column = (attribute as Sql).Column;
                }
                domainObjectPropertyNames[i] = column;
            }
            return domainObjectPropertyNames;
        }

        protected string GetFullyQualifiedTableName(out string identifierCase)
        {
            string schema = null, tablealias = null, casetemp = null;
            object[] attributes = typeof(TDomainObject).GetCustomAttributes(false);
            foreach (object attribute in attributes)
            {
                if (!(attribute is Sql)) continue;
                schema = (attribute as Sql).Schema;
                tablealias = (attribute as Sql).Table;
                casetemp = (attribute as Sql).IdentifierCase;
            }
            identifierCase = casetemp;
            return schema == null ? D(tablealias ?? _domainObjectName) : D(schema) + "." + D(tablealias ?? _domainObjectName);
        }

        protected string CreateSelectCommandString(out string sqlColumnNames)
        {
            string columns = "";
            for (int i = 0; i < _sqlColumnAliases.Length; i++)
            {
                if (i == _sqlColumnAliases.Length - 1) columns += _sqlColumnAliases[i] == null ? D(_domainObjectProperties[i].Name) + " " : D(_sqlColumnAliases[i]) + " AS " + D(_domainObjectProperties[i].Name) + " ";
                else columns += _sqlColumnAliases[i] == null ? D(_domainObjectProperties[i].Name) + ", " : D(_sqlColumnAliases[i]) + " AS " + D(_domainObjectProperties[i].Name) + ", ";
            }
            sqlColumnNames = columns;
            return "SELECT " + columns + " FROM " + _sqlFullyQualifiedTableName + ";";
        }

        protected string D(string identifier) //delimiter 
        {
            string idtemp = identifier;
            switch ((_identifierCase ?? "").ToUpper())
            {
                case "UPPER":
                    idtemp = identifier.ToUpper();
                    break;
                case "LOWER":
                    idtemp = identifier.ToLower();
                    break;
            }
            switch (typeof(TDbConnection).Name)
            {
                case "SqlConnection":
                    return '[' + idtemp + ']';
                case "MySqlConnection":
                    return '`' + idtemp + '`';
                default:
                    return '"' + idtemp + '"'; //ANSI Standard
            }
        }
    }

    public class Table<TDbConnection, TDomainObject> : View<TDbConnection, TDomainObject> where TDbConnection : DbConnection, new() where TDomainObject : DomainObject, new()
    {
        protected readonly string _sqlInsertCommand;
        protected readonly string _sqlUpdateCommand;

        public Table(string connectionString) : base(connectionString)
        {
            _sqlInsertCommand = CreateInsertCommandString();
            _sqlUpdateCommand = CreateUpdateCommandString();
        }

        public int Insert(TDomainObject domainObject)
        {
            using (var connection = new TDbConnection())
            {
                connection.ConnectionString = ConnectionString;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = _sqlInsertCommand;
                    PrepareParameters(command, domainObject);
                    try
                    {
                        connection.Open();
                        switch (typeof(TDbConnection).Name)
                        {
                            case "SqlConnection":
                                return Convert.ToInt32(command.ExecuteScalar());
                            case "MySqlConnection":
                                return Convert.ToInt32(command.ExecuteScalar());
                            case "NpgsqlConnection":
                                return Convert.ToInt32(command.ExecuteScalar());
                            case "SQLiteConnection":
                                return Convert.ToInt32(command.ExecuteScalar());
                            default:
                                return command.ExecuteNonQuery(); //other rdbms will return the number of rows affected instead
                        }
                    }
                    catch (DbException ex)
                    {
                        throw new ApplicationException(string.Format("There was an error inserting data into the {0} Table. Please check your connection.", _domainObjectName), ex);
                    }
                }
            }
        }

        public int Update(TDomainObject domainObject)
        {
            using (var connection = new TDbConnection())
            {
                connection.ConnectionString = ConnectionString;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = _sqlUpdateCommand;
                    PrepareParameters(command, domainObject);
                    try
                    {
                        connection.Open();
                        return command.ExecuteNonQuery();
                    }
                    catch (DbException ex)
                    {
                        throw new ApplicationException(string.Format("There was an error updating the {0} Table. Please check your connection.", _domainObjectName), ex);
                    }
                }
            }
        }

        public int Delete(TDomainObject domainObject)
        {
            int id = (int)_domainObjectProperties[0].GetValue(domainObject, null); //Get the value of the first property of domain object which is the id column
            return Delete(id);
        }

        public int Delete(int id)
        {
            using (var connection = new TDbConnection())
            {
                connection.ConnectionString = ConnectionString;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = string.Format("DELETE FROM {0} WHERE {1} = {2};", _sqlFullyQualifiedTableName, D(_sqlColumnAliases[0] ?? _domainObjectProperties[0].Name), id);
                    try
                    {
                        connection.Open();
                        return command.ExecuteNonQuery();
                    }
                    catch (DbException ex)
                    {
                        throw new ApplicationException(string.Format("There was an error deleting record with Id number: {0} from {1} Table. Please check your connection.", id, _domainObjectName), ex);
                    }
                }
            }
        }

        // HELPER METHODS
        protected string CreateInsertCommandString() // identity comlumn will not be included 
        {
            string intoPart = "INSERT INTO " + _sqlFullyQualifiedTableName + " (";
            string valuesPart = " VALUES (";
            for (int i = 0; i < _sqlColumnAliases.Length; i++)
            {
                if (i == 0) continue;
                if (i == _sqlColumnAliases.Length - 1)
                {
                    intoPart += D(_sqlColumnAliases[i] ?? _domainObjectProperties[i].Name) + ") ";
                    valuesPart += "@" + _domainObjectName + _domainObjectProperties[i].Name + ") ";
                }
                else
                {
                    intoPart += D(_sqlColumnAliases[i] ?? _domainObjectProperties[i].Name) + ", ";
                    valuesPart += "@" + _domainObjectName + _domainObjectProperties[i].Name + ", ";
                }
            }
            string primaryKey = _sqlColumnAliases[0] ?? _domainObjectProperties[0].Name;
            switch (typeof(TDbConnection).Name)
            {
                case "SqlConnection":
                    return intoPart + "OUTPUT INSERTED.[" + primaryKey + "] " + valuesPart + ";";
                case "MySqlConnection":
                    return intoPart + valuesPart + "; SELECT LAST_INSERT_ID();";
                case "NpgsqlConnection":
                    return intoPart + valuesPart + " RETURNING " + D(primaryKey) + ";";
                case "SQLiteConnection":
                    return intoPart + valuesPart + "; SELECT LAST_INSERT_ROWID();";
                default:
                    return intoPart + valuesPart + ";"; //for other rdbms, the inserted id will not be returned
            }
        }

        protected string CreateUpdateCommandString()
        {
            string updateCommand = "UPDATE " + _sqlFullyQualifiedTableName + " SET ";
            for (int i = 0; i < _sqlColumnAliases.Length; i++)
            {
                if (i == 0) continue;
                if (i == _sqlColumnAliases.Length - 1) updateCommand += D(_sqlColumnAliases[i] ?? _domainObjectProperties[i].Name) + " = @" + _domainObjectName + _domainObjectProperties[i].Name + " ";
                else updateCommand += D(_sqlColumnAliases[i] ?? _domainObjectProperties[i].Name) + " = @" + _domainObjectName + _domainObjectProperties[i].Name + ", ";
            }
            updateCommand += " WHERE " + D(_sqlColumnAliases[0] ?? _domainObjectProperties[0].Name) + " = @" + _domainObjectName + _domainObjectProperties[0].Name + ";";  //idColumn
            return updateCommand;
        }

        protected void PrepareParameters(DbCommand command, DomainObject domainObject)
        {
            foreach (PropertyInfo property in _domainObjectProperties)
            {
                DbParameter parameter = command.CreateParameter();
                parameter.ParameterName = "@" + _domainObjectName + property.Name;
                parameter.Value = property.GetValue(domainObject, null) ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
        }
    }
}
