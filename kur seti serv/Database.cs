using System.Data.SQLite;
using System.IO;
namespace kur_seti_serv
{
    class Database
    {
        public SQLiteConnection myConnection;

        public Database()
        {
            myConnection = new SQLiteConnection("Data Source=base.db");
            if(!File.Exists("./base.db"))
            {
                SQLiteConnection.CreateFile("base.db");
            }
        }
        public void openConnection()
        {
            if(myConnection.State!= System.Data.ConnectionState.Open)
            {
                myConnection.Open();
            }
        }
        public void closeConnection()
        {
            if (myConnection.State != System.Data.ConnectionState.Closed)
            {
                myConnection.Close();
            }
        }
    }
}
