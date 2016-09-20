using System;
using System.Windows.Forms;
using Owen.Orm;
using System.Data.SQLite;

namespace OwenOrmSample
{
    public partial class Form1 : Form
    {
        string connectionString = @"data source = Test.db; version = 3";
        Table<SQLiteConnection, Person> personTable;

        public Form1()
        {
            prepareDatabaseFile();
            InitializeComponent();
            personTable = new Table<SQLiteConnection, Person>(connectionString);
            refreshData();
        }

        private void refreshData()
        {
            dataGrid.DataSource = personTable.ToList(); //read all data from database
        }

        private void addButton_Click(object sender, EventArgs e)
        {
            var p = new Person
            {
                FirstName = firstName.Text,
                MiddleName = middleName.Text,
                LastName = lastName.Text,
                Birthday = birthday.Value
            };
            personTable.Insert(p);
            refreshData();
        }

        private void updateButton_Click(object sender, EventArgs e)
        {
            try
            {
                int id = (int)dataGrid.CurrentRow.Cells[0].Value;
                var p = new Person
                {
                    PersonId = id,
                    FirstName = firstName.Text,
                    MiddleName = middleName.Text,
                    LastName = lastName.Text,
                    Birthday = birthday.Value
                };
                personTable.Update(p);
                refreshData();
            }
            catch { }
        }

        private void deletebutton_Click(object sender, EventArgs e)
        {
            try
            {
                int id = (int)dataGrid.CurrentRow.Cells[0].Value;
                personTable.Delete(id);
                refreshData();
            }
            catch { }
        }

        void prepareDatabaseFile()
        {
            var q = new Query<SQLiteConnection, Blank>(connectionString, null);
            //create the table in our database
            q.ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Person(PersonId INTEGER PRIMARY KEY AUTOINCREMENT, FirstName VARCHAR(20), MiddleName VARCHAR(20), LastName VARCHAR(20), Birthday  DATETIME);");
        }

        //i just need this to execute a query
        class Blank : DomainObject { }
    }

    public class Person : DomainObject
    {
        public int PersonId { get; set; }
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public string LastName { get; set; }
        public DateTime? Birthday { get; set; }
    }
}
