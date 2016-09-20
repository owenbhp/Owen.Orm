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
            personTable = new Table<SQLiteConnection, Person>(connectionString);
            prepareDatabaseFile();
            
            InitializeComponent();
            refreshData();
        }

        private void refreshData()
        {
            searchBox.Clear();
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
            //let's create our table using this handy method ;)
            personTable.ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Person(PersonId INTEGER PRIMARY KEY AUTOINCREMENT, FirstName VARCHAR(20), MiddleName VARCHAR(20), LastName VARCHAR(20), Birthday  DATETIME);");
        }

        private void refreshButton_Click(object sender, EventArgs e)
        {
            refreshData();
        }

        private void searchBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            dataGrid.DataSource = personTable.ToList("select {this} where firstname like {0} or lastname like {0} or middlename like {0}", "%" + searchBox.Text + "%");
        }
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
