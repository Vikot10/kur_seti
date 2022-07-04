using System;
using System.Collections.Generic;
using NAudio.Wave;
using System.Text;
using System.Data.SQLite;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace kur_seti_serv
{
    public partial class Form1 : Form
    {
        Socket MainSocket;
        //private static ManualResetEvent _stopper = new ManualResetEvent(false);
        List<Room> _rooms = new List<Room>();
        List<string> _clientName=new List<string>();
        WaitRoom _waitRooms = new WaitRoom();
        int maxElInRoom = 20;
        Database db;
        Thread tr;
        WaveOut waveOut;
        BufferedWaveProvider provider;
        IPAddress myAdress;
        int myPort=900;
        public Form1()
        {
            InitializeComponent();
            //myAdress = Dns.GetHostAddresses(Dns.GetHostName())[1];
            myAdress = IPAddress.Parse("127.0.0.1");
            db = new Database();
            MainSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            MainSocket.Bind(new IPEndPoint(myAdress, myPort));
            MainSocket.Listen(50);
            tr = new Thread(delegate () { ConnectClient(); });
            tr.IsBackground = true;
            tr.Start();
            tr.Suspend();
            waveOut = new WaveOut();
            waveOut.DesiredLatency = 100;
            provider = new BufferedWaveProvider(new WaveFormat(8000, 1));
            waveOut.Init(provider);
            waveOut.Play();
            CreateRoom("Work Room");
        }
        string UserVerification(string request)
        {
            string[] vs = request.Split('|');
            if(vs[0]=="S")
            {
                string answer;
                string query = $"SELECT * FROM mainTable WHERE login='{vs[1]}'";
                SQLiteCommand sQLiteCommand = new SQLiteCommand(query,db.myConnection);
                db.openConnection();
                SQLiteDataReader reader = sQLiteCommand.ExecuteReader();
                reader.Read();
                if (reader.HasRows)
                {
                    if (reader["password"].ToString() == vs[2])
                    {
                        answer = "OK|" + reader["name"];//+ "|" + reader["login"] + "|" + reader["password"];
                    }
                    else
                    {
                        answer = "Failed to login|";
                    }
                }
                else
                {
                    answer = "Failed to login|";
                }
                db.closeConnection();
                return answer;
            }
            else
            {
                string answer;
                try
                {
                    string query = "INSERT INTO mainTable ('login','password','name') VALUES (@login,@password,@name)";
                    SQLiteCommand sQLiteCommand = new SQLiteCommand(query, db.myConnection);
                    db.openConnection();
                    sQLiteCommand.Parameters.AddWithValue("@login", vs[1]);
                    sQLiteCommand.Parameters.AddWithValue("@password", vs[2]);
                    sQLiteCommand.Parameters.AddWithValue("@name", vs[1]);
                    sQLiteCommand.ExecuteNonQuery();
                    db.closeConnection();
                    answer = $"OK|{vs[1]}";//|{vs[1]}|{vs[2]}";
                }
                catch(Exception ex)
                {
                    answer= "Login already exists|";
                }
                return answer;
            }
        }
        private string ResponseToMe(ref Socket temp)
        {
            StringBuilder builder = new StringBuilder();
            int bytes = 0;
            byte[] data = new byte[1024];
            temp.ReceiveTimeout = 1000;
            while (true)
            {
                try
                {
                    bytes = temp.Receive(data, data.Length, 0);
                    builder.Append(Encoding.Unicode.GetString(data, 0, bytes));
                    Console.WriteLine(builder.ToString());
                }
                catch (Exception ex)
                {
                    break;
                }
            }
            //do
            //{
            //    bytes = temp.Receive(data, data.Length, 0);
            //    builder.Append(Encoding.UTF8.GetString(data, 0, bytes));
            //}
            //while (temp.Available > 0);
            return builder.ToString();
        }

        void ResponseFromMe(ref Socket temp,string message)
        {
            try
            {
                byte[] byteMessage = Encoding.Unicode.GetBytes(message);
                temp.Send(byteMessage, byteMessage.Length, 0);
            }
            catch
            {

            }
        }

        void ConnectClient()
        {
            while (true)
            {
                Socket handler = MainSocket.Accept();
                string[] answer = UserVerification(ResponseToMe(ref handler)).Split('|');// Ok|name|log|pass
                if(answer[0]=="OK")
                {
                    if (_clientName.Exists(x => x == answer[1]))
                    {
                        ResponseFromMe(ref handler, "Already online|");
                    }
                    else
                    {
                        _clientName.Add(answer[1]);
                        Thread t = new Thread(delegate () { DoClient(handler,answer); });
                        t.IsBackground=true;
                        t.Start();
                    }
                }
                else
                {
                    ResponseFromMe(ref handler, answer[0]);
                }
                
            }
        }
        void DoClient(Socket handler,string[] answer)
        {
            string name=answer[1];
            Client client = new Client(answer[1]);
            string nameRoom="";
            int bytes;
            byte[] data = new byte[256];
            Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(myAdress, 0));
            socket.Listen(1);
            string[] sock =  socket.LocalEndPoint.ToString().Split(':');
            ResponseFromMe(ref handler, $"{answer[0]}|{answer[1]}|{sock[sock.Length-1]}"); // OK|name|Port
            client.s = socket.Accept();
            handler.Dispose();
           
            while (true)
            { 
                StringBuilder builder = new StringBuilder();
                string message = "";
                try
                {
                    do
                    {
                        try
                        {
                            bytes = client.s.Receive(data, data.Length, 0);
                            builder.Append(Encoding.Unicode.GetString(data, 0, bytes));
                        }
                        catch (Exception ex)
                        {
                            if(nameRoom=="")
                            {
                                _clientName.Remove(name);
                                RemoveFromWaitRoom(client);
                            }
                            else
                            {
                                _clientName.Remove(name);
                                int index = FindRoom(nameRoom);
                                RemoveFromRoom(index, client);
                                SendMessageInWaitRoom($"Change|{nameRoom}|{_rooms[index].size}");
                                SendMessageAllInRoom(index, $"DelClient|{ client.name}");
                            }
                            return;
                        }
                    }
                    while (client.s.Available > 0);
                    string[] vs = builder.ToString().Split('|');


                    switch (vs[0])
                    {
                        case "Create":
                            {
                                if (_rooms.Exists(x=>x.name==vs[1]))
                                    message = "Create|Failed";
                                else
                                {
                                    nameRoom = vs[1];
                                    RemoveFromWaitRoom(client);
                                    CreateRoom(vs[1]);
                                    int index = FindRoom(nameRoom);
                                    AddInRoom(index, client);
                                    message = $"Connect|{_rooms[index].portUDP}";
                                    SendMessageInWaitRoom($"Create|{nameRoom}&1");
                                }
                                ResponseFromMe(ref client.s, message);
                            }
                            break;
                        case "Connect":
                            {
                                int index = FindRoom(vs[1]);
                                if (index == -1)
                                    message = "Connect|Failed";
                                else
                                {
                                    nameRoom = vs[1];
                                    message = $"Connect|{_rooms[index].portUDP}";
                                    for (int i = 0; i < _rooms[index].size; i++)
                                    {
                                        message += $"|{_rooms[index].listClient[i].name}&{_rooms[index].listClient[i].status}";
                                    }
                                    RemoveFromWaitRoom(client);
                                    AddInRoom(index, client);
                                    SendMessageInRoom(index, client.name, $"NewClient|{ client.name}&");
                                    SendMessageInWaitRoom($"Change|{nameRoom}|{_rooms[index].size}");
                                }
                                ResponseFromMe(ref client.s, message);
                            }
                            break;
                        case "OK":
                            client.Udp = new IPEndPoint(myAdress,Int32.Parse(vs[1]));
                            for (int i = 0; i < _rooms.Count; i++)
                            {
                                message += $"{_rooms[i].name}&{_rooms[i].size}|";
                            }
                            nameRoom = "";
                            AddInWaitRoom(client);
                            ResponseFromMe(ref client.s, message);
                            break;
                        case "Speak":
                            {
                                int index = FindRoom(nameRoom);
                                client.status = "Speak";
                                SendMessageAllInRoom(index, $"Speak|{ client.name}");
                            }
                            break;
                        case "LeaveMe":
                            {
                                int index = FindRoom(nameRoom);
                                client.status = "";
                                RemoveFromRoom(index, client);
                                SendMessageInWaitRoom($"Change|{nameRoom}|{_rooms[index].size}");
                                AddInWaitRoom(client);
                                SendMessageAllInRoom(index, $"DelClient|{ client.name}");
                                message = "LeaveMe|";
                                for (int i = 0; i < _rooms.Count; i++)
                                {
                                    message += $"{_rooms[i].name}&{_rooms[i].size}|";
                                }
                                ResponseFromMe(ref client.s, message);
                                nameRoom = "";
                            }
                            
                            break;
                        case "StopSpeak":
                            {
                                client.status = "";
                                int index = FindRoom(nameRoom);
                                SendMessageAllInRoom(index, $"StopSpeak|{ client.name}");
                            }
                            break;
                        case "Mute":
                            {
                                client.status = "Mute";
                                int index = FindRoom(nameRoom);
                                SendMessageAllInRoom(index, $"Mute|{ client.name}");
                            }
                            break;
                        case "Unmute":
                            {
                                client.status = "";
                                int index = FindRoom(nameRoom);
                                SendMessageAllInRoom(index, $"StopSpeak|{ client.name}");
                            }
                            break;
                        case "Message":
                            {
                                int index = FindRoom(nameRoom);
                                SendMessageAllInRoom(index, $"Message|{client.name}: {vs[1]}");
                            }
                            break;
                    }
                    //Action action = () => treeView1.Nodes.Add(builder.ToString());
                    //if (InvokeRequired)
                    //{
                    //    Invoke(action);
                    //}
                    //else
                    //{
                    //    action();
                    //}
                }
                catch (Exception ex)
                {
                    break;
                }
            }               
        }
        private int FindRoom(string nameRoom)
        {
            int i = _rooms.FindIndex(o => o.name == nameRoom);

            if(_rooms[i].size==maxElInRoom)
            {
                return -1;
            }
            else
            {
                return i;
            }
        }
        private void AddInWaitRoom(Client cl)
        {
            _waitRooms.listTcp.Add(cl.s);
            _waitRooms.size++;
        }
        private void RemoveFromWaitRoom(Client cl)
        {
            _waitRooms.listTcp.Remove(cl.s);
            _waitRooms.size--;
        }
        private void SendMessageInWaitRoom(string message)
        {
            for(int i=0; i < _waitRooms.size;i +=20)
            {
                int k = i;
                Thread t = new Thread(delegate () { SendInWaitRoom(k,message); });
                t.Start();               
            }
        }
        private void SendInWaitRoom(int i,string message)
        {
            int dopi = 0;
            while(dopi<20 && i<_waitRooms.size)
            {              
                byte[] byteMessage = Encoding.Unicode.GetBytes(message);
                _waitRooms.listTcp[i].Send(byteMessage, byteMessage.Length, 0);
                dopi++;
                i++;
            }
        }
        private void SendMessageAllInRoom(int index, string message)
        {
            for (int i = 0; i < _rooms[index].size; i++)
            {
                 byte[] byteMessage = Encoding.Unicode.GetBytes(message);
                 _rooms[index].listClient[i].s.Send(byteMessage, byteMessage.Length, 0);
            }
        }
        private void SendMessageInRoom(int index,string name,string message)
        {
            for (int i = 0; i < _rooms[index].size;i++)
            {
                if (_rooms[index].listClient[i].name != name)
                {
                    byte[] byteMessage = Encoding.Unicode.GetBytes(message);
                    _rooms[index].listClient[i].s.Send(byteMessage, byteMessage.Length, 0);
                }

            }
        }

        private void CreateRoom(string nameRoom)
        {           
            Socket socket1 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint localIP = new IPEndPoint(myAdress, 0);
            socket1.Bind(localIP);
            _rooms.Add(new Room(nameRoom,Int32.Parse(socket1.LocalEndPoint.ToString().Split(':')[1])));
            Thread t = new Thread(delegate () { ResponceUdp(socket1,_rooms.Count-1); });
            t.IsBackground=true;
            t.Start();
        }

        private void ResponceUdp(Socket socket1,int index)
        {

            while (true)
            {
                int bytes = 0; // количество полученных байтов

                EndPoint remoteIp = new IPEndPoint(IPAddress.Any, 0);
                do
                {
                    try
                    {
                        byte[] data = new byte[160];
                        bytes = socket1.ReceiveFrom(data, ref remoteIp);
                        if (bytes != 0)
                            SendSpeakUdp(socket1, data, index, remoteIp);
                    }
                    catch (Exception ex)
                    {
                        break;
                    }
                }
                while (socket1.Available > 0);

            }
        }
        private void SendSpeakUdp(Socket socket1,byte[] data,int index, EndPoint howSend)
        {
            for(int i=0;i<_rooms[index].size;i++)
            {
                if(_rooms[index].listClient[i].Udp.ToString()!=howSend.ToString() && _rooms[index].listClient[i].status!="Mute")
                {
                    socket1.SendTo(data, _rooms[index].listClient[i].Udp);
                }
            }
        }

        private void AddInRoom(int index,Client cl)
        {
            _rooms[index].listClient.Add(cl);
            _rooms[index].size++;
        }

        private void RemoveFromRoom(int index,Client cl)
        {
            _rooms[index].listClient.Remove(cl);
            _rooms[index].size--;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (button1.Text[0]=='В')
            {
               //MainSocket.Blocking = true;              
                tr.Resume();
                //_stopper.Close();
                treeView1.Nodes.Add(tr.ThreadState.ToString());
                button1.Text = "Отключить";
                
            }
            else
            {
                //_stopper.Set();
                //MainSocket.Blocking = false;
                tr.Suspend();
                treeView1.Nodes.Add( tr.ThreadState.ToString());
                button1.Text = "Включить";
            }
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            
        }
    }
    class Client
    {
        public string status="";
        public EndPoint Udp;
        public Socket s;
        public string name;
        public Client(string n)
        {
            name = n;
        }
    }
    class WaitRoom
    {
        public int size=0;
        public  List<Socket> listTcp;
        public WaitRoom()
        {
            listTcp = new List<Socket>();
        }
    }
    class Room
    {
        public int portUDP;
        public int size = 0;
        public string name;
        public List<Client> listClient;
        public Room(string n,int portU)
        {
            name = n;
            portUDP = portU;
            listClient=new List<Client>();
        }
    }
}
