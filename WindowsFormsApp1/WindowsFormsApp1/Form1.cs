using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Data.OleDb;

using System.Net;
using System.Net.Sockets;
using MySql.Data.MySqlClient;

namespace Server01
{
    public partial class Form1 : Form
    {
        Socket server;
        public int bufferCount = 0;
        StateObject[] stateOs;
        int maxConn = 10;
        int index = 0, num = 0;
        List<RoomObject> roomList;
        List<RoomObject> freeRoomList;


        System.Timers.Timer timer = new System.Timers.Timer(1000);
        public long heartBeatTime = 4;

        byte[] sendBuff = new byte[1024];

        public Form1()
        {
            InitializeComponent();
            System.Windows.Forms.Control.CheckForIllegalCrossThreadCalls = false;
            roomList = new List<RoomObject>();
            roomList.Clear();
            freeRoomList = new List<RoomObject>();
        }

        public void HandleMainTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            long timeNow = Sys.GetTimeStamp();
            for (int i = 0; i < index; i++)
            {
                if (stateOs[i] == null) continue;
                if (stateOs[i].lastTickTime < timeNow - heartBeatTime)
                {
                    DeleteRoom(roomList, stateOs[i]);
                    stateOs[i].sock.Shutdown(SocketShutdown.Both);
                    System.Threading.Thread.Sleep(30);
                    lock(stateOs[i].sock)
                    stateOs[i].sock.Close(); 
                    DeleteClient(stateOs, i);
                }
            }
            showRoomList(roomList);
            CopyFreeRoomList();
            SendRoomList(freeRoomList);
            timer.Start();
        }
        private void DeleteClient(StateObject[] clients, int i)
        {
            if (index == 0) return;
            lock (clients)
            {
                for (int j = i; j < index - 1; j++)
                {
                    clients[j] = clients[j + 1];
                }
                index--;
            }
        }
        private void DeleteRoom(List<RoomObject> rooms, StateObject client)
        {
            //foreach (RoomObject room in rooms)
            for(int i=rooms.Count-1; i>=0; i--)
            {
                if (client == rooms[i].client0)
                    rooms.Remove(rooms[i]);
            }
        }
        public void SendRoomList(List<RoomObject> rooms)
        {
            String str = "roomList ";
            str += (rooms.Count).ToString();
            foreach (RoomObject room in rooms)
            {
                if (!room.playing)
                {
                    str += " ";
                    str += ((IPEndPoint)(room.client0.sock.RemoteEndPoint)).ToString();
                }
            }

            byte[] sendBuff = new byte[1024];
            sendBuff = System.Text.Encoding.ASCII.GetBytes(str);

            for (int i = 0; i < index; i++)
            {
                try
                {
                    stateOs[i].sock.Send(sendBuff);
                    textBox1.Text = "发送:" + str + "To:" + stateOs[i].sock.RemoteEndPoint.ToString();
                }
                catch (System.Exception ex)
                {
                    textBox1.Text = ex.ToString();
                }
            }


            
        }
        public void showRoomList(List<RoomObject> rooms)
        {
            String str = "";
            str = index.ToString() + "\r\n";
            for (int i = 0; i < index; i++)
            {
                str += stateOs[i].sock.RemoteEndPoint.ToString();
                str += "\r\n";
            }
            textBox1.Text = str;
        }        
        private void button1_Click(object sender, EventArgs e)
        {
            //暂时定义服务器的ip地址
            string serverIP = "127.0.0.1";

            IPAddress local = IPAddress.Parse(serverIP);
            int iLocalPort = int.Parse(txtListenPort.Text);
            IPEndPoint iep = new IPEndPoint(local, iLocalPort);
            stateOs = new StateObject[maxConn];

            //创建服务器的socket对象

            server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                server.Bind(iep);
                server.Listen(10);
                server.BeginAccept(new AsyncCallback(acceptCb), server);
                this.textBox1.Text = "开始监听"+iLocalPort.ToString()+"号端口\r\n";
                button1.Enabled = false;
                txtListenPort.Enabled = false;
            }
            catch (Exception ex)
            {
                this.textBox1.Text = "该端口已被占用\r\n";
            }

            timer.Elapsed += new System.Timers.ElapsedEventHandler(HandleMainTimer);
            timer.AutoReset = false;
            timer.Enabled = true;
        }
        
        public void acceptCb(IAsyncResult iar)
        {
            Socket MyServer = (Socket)iar.AsyncState;
                      
            stateOs[index] = new StateObject();

            textBox2.Text = index.ToString();

            stateOs[index].sock = MyServer.EndAccept(iar);
            textBox1.Text += "有新的连接建立"+index.ToString()+stateOs[index].sock.RemoteEndPoint.ToString()+"\r\n";  
            try
            {
                stateOs[index].sock.BeginReceive(stateOs[index].buffer, 0, StateObject.BUFFER_SIZE, 0, new AsyncCallback(receiveCb), stateOs[index]);
            }
            catch (Exception e)
            {  }
            MyServer.BeginAccept(new AsyncCallback(acceptCb), server);
            index++;
            if (index > 10)
            {
                textBox1.Text = "连接池已满";
                index = 0;
            }

            
        }
        public void receiveCb(IAsyncResult ar)
        {
            String recvStr = "";           
            StateObject stateo1 = (StateObject)ar.AsyncState;
            Socket client = stateo1.sock;
            if (client.Connected == false) return;
            try
            {
                int recNum = stateo1.sock.EndReceive(ar);
                recvStr = System.Text.Encoding.ASCII.GetString(stateo1.buffer, 0, recNum);
                
                if (recvStr.IndexOf("\0") != -1)
                {
                    recvStr = recvStr.Substring(0,recvStr.IndexOf("\0"));
                }
                

                string[] args = recvStr.Split(' ');
                if ("createRoom" == args[0]) 
                {
                    NewRoom(stateo1);

                }
                if ("heartBeat" == args[0])
                {
                    textBox1.Text = "收到一个心跳包" + num.ToString(); num++;                  
                    stateo1.lastTickTime = Sys.GetTimeStamp();
                }
                //textBox1.Text = recvStr;
                //对接收协议的判断
                if ("Regist" == args[0])
                {
                    RegistHandle(args,stateo1);
                }
                else if("Login" == args[0])
                {
                    LoginHandle(args,stateo1);
                }

                if("getUserNameByMysql" == args[0])
                {
                    GetByNameByMysql(args, stateo1);
                }

                if ("enterRoom" == args[0])
                {
                    EnterRoom(args[1], stateo1);
                    textBox2.Text += recvStr;
                }
                
                if ("Position" == args[0]|| args[0] == "Flip" || "Fire" == args[0])
                {
                    foreach (RoomObject room in roomList)
                    {
                        
                        if (client == room.client0.sock)
                        {
                            room.client1.sock.Send(stateo1.buffer);

                        } 
                        else if (client == room.client1.sock)
                        {
                            room.client0.sock.Send(stateo1.buffer);
                        }
                        
                    }
                }
                if("Win" == args[0])
                {

                    byte[] sendBuff = new byte[1024];
                    sendBuff = System.Text.Encoding.Default.GetBytes("Fail ");
                    foreach (RoomObject room in roomList)
                    {
                        if (client == room.client0.sock)
                        {
                            room.client0.sock.Send(stateo1.buffer);
                            room.client1.sock.Send(sendBuff);

                        }
                        else if (client == room.client1.sock)
                        {
                            room.client1.sock.Send(stateo1.buffer);
                            room.client0.sock.Send(sendBuff);
                        }
                    }
                }
                if(args[0] == "Exit")
                {
                    //将数据库中的online置0
                    Exit(args, stateo1);
                }
                    

                client.BeginReceive(stateo1.buffer, 0, StateObject.BUFFER_SIZE, 0, receiveCb, stateo1);
            }

            catch (System.Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
         
        }

        //server收到client发送的createroom请求，创建房间信息放在roomList中
        public void NewRoom(StateObject obj0)
        {
            //obj0 --- stateol
            RoomObject room = new RoomObject();
            room.CreateRoom(obj0);
            roomList.Add(room);
        }
        public void EnterRoom(String strClient0, StateObject obj1)
        {
            byte[] sendBuff = new byte[1024];
            //将roomList中已经创建的房间信息读取出来
            foreach (RoomObject room in roomList)
            {

                if (room.client0.sock.RemoteEndPoint.ToString() == strClient0)
                {
                    room.EnterRoom(obj1);

                    //向自己的客户端发送beginGame 0 协议，表示在创建房间人的客户端受到的编号为0

                    sendBuff = System.Text.Encoding.UTF8.GetBytes("beginGame 0 ");
                    room.client0.sock.Send(sendBuff);

                    sendBuff = System.Text.Encoding.UTF8.GetBytes("beginGame 1 ");
                    //向对方的客户端发送协议，表示对方客户端接收到的编号为1
                    room.client1.sock.Send(sendBuff);


                    string str ="创建和进入地址" + room.client0.sock.RemoteEndPoint.ToString() + "----" + room.client1.sock.RemoteEndPoint.ToString();
                    textBox2.Text = str;

                }
            }                     
        }
        void CopyFreeRoomList()
        {
            freeRoomList.Clear();
            foreach (RoomObject room in roomList)
            {
                if (!room.playing)
                {
                    RoomObject tempRoom = new RoomObject();
                    tempRoom = room;
                    freeRoomList.Add(tempRoom);
                }                    
            }
        }

        void GetByNameByMysql(string[] args, StateObject stateo1)
        {
            byte[] sendBuff = new byte[1024];

            string username = "";

            string ip = args[1].Split(':')[0];
            string port = args[1].Split(':')[1];


            //连接myql
            MySqlConnection conn = new MySqlConnection("server=localhost;port=3306;user=root;password=yangliu1999;database=network;");
            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT username FROM ipv4_int WHERE ip='" + ip +"' AND port=" + port +"";
            MySqlDataReader odrReader = cmd.ExecuteReader();

            odrReader.Read();
            username = odrReader[0].ToString();
            if(username != null)
            {
                sendBuff = System.Text.Encoding.ASCII.GetBytes("GetNameSuccessed " + username + " " + args[1] + " ");
                stateo1.sock.Send(sendBuff);
            }
            else
            {
                sendBuff = System.Text.Encoding.ASCII.GetBytes("GetNameFail ");
                stateo1.sock.Send(sendBuff);
            }
                
        }

        void RegistHandle(string[] args, StateObject stateo1)
        {
            byte[] sendBuff = new byte[1024];

            string userName = args[1];
            string passWordMD5 = args[2];
            string ipAddressPort = args[3];
            string ip = ipAddressPort.Split(':')[0];
            int port = Int32.Parse(ipAddressPort.Split(':')[1]);
            
            //查询是否有同名记录

            MySqlConnection conn = new MySqlConnection("server=localhost;port=3306;user=root;password=yangliu1999;database=network;");
            conn.Open();
            
            MySqlCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM user1 WHERE username='" + userName + "'";
            MySqlDataReader odrReader = cmd.ExecuteReader();

            if (odrReader.HasRows)   //有同名记录
            {
                sendBuff = System.Text.Encoding.ASCII.GetBytes("RegistFail 1 ");
                stateo1.sock.Send(sendBuff);                
            }
            else  //没有同名记录，入库
            {
                conn.Close();
                conn.Open();
                
                string sql = "INSERT INTO user1 (username,userpassword,online,score) VALUES ('" + userName + "','" + passWordMD5 + "',1,0)";
                string mysqlInsert = "INSERT INTO ipv4_int (username,ip,port) VALUES ('" + userName + "','" + ip + "'," + port + ")";
                MySqlCommand cmd1 = new MySqlCommand(sql, conn); //定义Command对象
                cmd1.ExecuteNonQuery(); //执行Command命令

                cmd1 = new MySqlCommand(mysqlInsert, conn); //定义Command对象
                cmd1.ExecuteNonQuery(); //执行Command命令

                sendBuff = System.Text.Encoding.ASCII.GetBytes("RegistSuccess ");
                stateo1.sock.Send(sendBuff);
            }
            conn.Close();
        }

        void LoginHandle(string[] args, StateObject stateo1)
        {
            byte[] sendBuff = new byte[1024];
            string userName = args[1];
            string passWordMD5 = args[2];
            string ipAddressPort = args[3];
            string ip = ipAddressPort.Split(':')[0];
            int port = Int32.Parse(ipAddressPort.Split(':')[1]);
            //查询是否有同名记录

            MySqlConnection conn = new MySqlConnection("server=localhost;port=3306;user=root;password=yangliu1999;database=network;");
            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM user1 WHERE username='" + userName + "'";
            MySqlDataReader odrReader = cmd.ExecuteReader();

            //有同名记录
            if (odrReader.HasRows)
            {
                odrReader.Read();
                string name = odrReader[0].ToString();
                string password = odrReader[1].ToString();
                string online = odrReader[2].ToString();
                if (name.Equals(userName) && password.Equals(passWordMD5))
                {
                    if (online.Equals("1"))
                    {
                        //已经在线
                        sendBuff = System.Text.Encoding.UTF8.GetBytes("LoginFail 3 ");
                        stateo1.sock.Send(sendBuff);
                    }
                    else if (online.Equals("0"))
                    {
                        //登录成功
                        sendBuff = System.Text.Encoding.UTF8.GetBytes("LoginSuccess ");
                        stateo1.sock.Send(sendBuff);

                        

                        conn.Close();
                        conn.Open();
                        //将ip和port写入数据库
                        string mysqlUpdate = "UPDATE ipv4_int SET ip='" + ip + "',port=" + port + " WHERE username='" + userName + "'";
                        MySqlCommand cmd1 = new MySqlCommand(mysqlUpdate, conn); //定义Command对象
                        cmd1.ExecuteNonQuery(); //执行Command命令

                        conn.Close();
                        conn.Open();

                        //登录成功修改状态
                        mysqlUpdate = "UPDATE user1 SET online='" + 1 + "' WHERE username='" + userName + "'";
                        cmd1 = new MySqlCommand(mysqlUpdate, conn); //定义Command对象
                        cmd1.ExecuteNonQuery(); //执行Command命令

                    }

                }
                else
                {
                    //密码不正确
                    sendBuff = System.Text.Encoding.UTF8.GetBytes("LoginFail 2 ");
                    stateo1.sock.Send(sendBuff);
                }

            }
            else
            {
                //用户名不存在
                sendBuff = System.Text.Encoding.UTF8.GetBytes("LoginFail 1 ");
                stateo1.sock.Send(sendBuff);
            }
            conn.Close();
        }
        void Exit(string[] args, StateObject stateo1)
        {
            string userName = args[1];
            MySqlConnection conn = new MySqlConnection("server=localhost;port=3306;user=root;password=yangliu1999;database=network;");
            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM user1 WHERE username='" + userName + "'";
            MySqlDataReader odrReader = cmd.ExecuteReader();

            if (odrReader.HasRows)
            {
                odrReader.Read();
                string name = odrReader[0].ToString();
                if(name == userName)
                {
                    conn.Close();
                    conn.Open();

                    string mysqlUpdate = "UPDATE user1 SET online='" + 0 + "' WHERE username='" + userName + "'";
                    MySqlCommand cmd1 = new MySqlCommand(mysqlUpdate, conn); //定义Command对象
                    cmd1.ExecuteNonQuery(); //执行Command命令
                }
            }
        }

        private void textBox1_TextChanged(object sender, System.EventArgs e)
        {

        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void Form1_Load(object sender, System.EventArgs e)
        {
            Console.Write("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        }


    }

    internal class OleDBCommand
    {
    }

    public class StateObject
    {
        public const int BUFFER_SIZE = 1024;
        public byte[] buffer = new byte[BUFFER_SIZE];
        public Socket sock = null;
        public bool bIsUsed = false;

        public long lastTickTime;
        public StateObject() { lastTickTime = Sys.GetTimeStamp(); }
    }
    public class RoomObject
    {
        public StateObject client0;
        public StateObject client1;
        public bool playing;
        public RoomObject()
        {
            client0 = new StateObject();
            client1 = new StateObject();
        }
        public void CreateRoom(StateObject c0) { client0 = c0; playing = false; }
        public void EnterRoom(StateObject c1) { client1 = c1; playing = true; }
    }
    public class Sys
    {
        public static long GetTimeStamp()
        {
            TimeSpan ts = DateTime.UtcNow - new DateTime(1907, 1, 1, 0, 0, 0, 0);
            return Convert.ToInt64(ts.TotalSeconds);
        }
        
    }
}
