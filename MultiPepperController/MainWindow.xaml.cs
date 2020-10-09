using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MultiPepperController
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        public bool runSendDataThread { get; set; }
        private Thread sendTouchDataThread { get; set; }
        private int serverPort { get; set; }
        private List<StateObject> activeConnections { get; set; }
        private object lockObjectConnection { get; set; }
        private Socket listener { get; set; }
        private List<string> commandList = new List<string>();

        public MainWindow()
        {
            InitializeComponent();
            lockObjectConnection = new object();
            activeConnections = new List<StateObject>();

            serverPort = Properties.Settings.Default.LastPort;
            textBoxPort.Text = serverPort.ToString();

            runSendDataThread = false;

        }

        private void textBoxPort_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            bool tmpParse = false;
            {
                float tmpF;
                var tmp = textBoxPort.Text + e.Text;
                tmpParse = Single.TryParse(tmp, out tmpF);
            }
            e.Handled = !tmpParse;
        }

        public class StateObject
        {
            public Socket workSocket = null;
            public const int BufferSize = 1024;
            public byte[] buffer = new byte[BufferSize];
            public StringBuilder sb = new StringBuilder();
            public bool sendDataFlag = false;
        }


        public void StartListening()
        {
            //IPAddress ipAddress = IPAddress.Parse(GetIPAddress("localhost"));
            IPAddress ipAddress = IPAddress.Parse(GetIPAddress(Dns.GetHostName()));

            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, serverPort);
            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);
                listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

        }

        public void StopListening()
        {
            listener.Close();
            listener = null;
        }

        public void AcceptCallback(IAsyncResult ar)
        {
            try
            {
                Socket listener = (Socket)ar.AsyncState;
                Socket handler = listener.EndAccept(ar);
                StateObject state = new StateObject();
                state.workSocket = handler;
                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReadCallback), state);
                activeConnections.Add(state);
                ChangeConnectionNumber();
                Console.WriteLine("there is {0} connections", activeConnections.Count);
                listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);
            }
            catch (System.ObjectDisposedException)
            {
                System.Console.WriteLine("Connection closed.");
                return;
            }

        }

        public void ReadCallback(IAsyncResult ar)
        {
            String content = String.Empty;
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;
            try
            {
                int bytesRead = handler.EndReceive(ar);
                if (bytesRead > 0)
                {
                    state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
                    content = state.sb.ToString();
                    if (content.IndexOf("\n") > -1 || content.IndexOf("<EOF>") > -1)
                    {
                        string getString = content.Replace("\r", "").Replace("\n", "");
                        if (getString == "Start")
                        {
                            Console.WriteLine("Start sending data");
                            state.sendDataFlag = true;
                        }
                        else if (getString == "Stop")
                        {
                            Console.WriteLine("Stop sending data");
                            state.sendDataFlag = false;
                        }
                        state.sb.Length = 0; ;
                        handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                            new AsyncCallback(ReadCallback), state);
                    }
                    else
                    {
                        handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                            new AsyncCallback(ReadCallback), state);
                    }
                }
                if (bytesRead == 0)
                {
                    lock (lockObjectConnection)
                    {
                        Console.WriteLine("Disconnected?");
                        activeConnections.Remove(state);
                        ChangeConnectionNumber();

                    }
                }
            }
            catch (Exception e)
            {
                lock (lockObjectConnection)
                {
                    Console.WriteLine(e.Message);
                    activeConnections.Remove(state);
                    ChangeConnectionNumber();
                }
            }

        }

        private void Send(Socket handler, String data)
        {
            byte[] byteData = Encoding.ASCII.GetBytes(data);
            handler.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), handler);
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                Socket handler = (Socket)ar.AsyncState;
                int bytesSent = handler.EndSend(ar);

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private void SendMessageThread()
        {
            while (runSendDataThread == true)
            {
                try
                {
                    lock (lockObjectConnection)
                    {
                        foreach (StateObject each in activeConnections)
                        {
                            if (each.sendDataFlag == true)
                            {
                                Send(each.workSocket, "Connected\n");
                            }

                            if(commandList.Count>0)
                            {
                                foreach (var item in commandList)
                                {
                                    Send(each.workSocket, item);
                                }
                            }
                        }
                        if (commandList.Count > 0)
                        {
                            commandList.Clear();
                        }

                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
                Thread.Sleep(33);
            }
            lock (lockObjectConnection)
            {
                foreach (StateObject each in activeConnections)
                {
                    each.workSocket.Close();
                }
            }
        }


        private string GetIPAddress(string hostname)
        {
            IPHostEntry host;
            host = Dns.GetHostEntry(hostname);

            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return string.Empty;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            runSendDataThread = false;
            Properties.Settings.Default.LastPort = serverPort;
            Properties.Settings.Default.Save();
        }

        private void buttonServerStart_Click(object sender, RoutedEventArgs e)
        {
            int tmpPort = 0;
            if (int.TryParse(textBoxPort.Text, out tmpPort) == false)
            {
                return;
            }
            serverPort = tmpPort;
            if (runSendDataThread == false)
            {
                ChangeConnectionNumber();
                buttonServerStart.Content = "Stop";
                StartListening();
                runSendDataThread = true;
                sendTouchDataThread = new Thread(new ThreadStart(SendMessageThread));
                sendTouchDataThread.Start();
            }
            else
            {
                runSendDataThread = false;
                sendTouchDataThread.Join();
                sendTouchDataThread = null;
                StopListening();
                lock (lockObjectConnection)
                {
                    activeConnections.RemoveRange(0, activeConnections.Count);
                }
                buttonServerStart.Content = "Start";


                ChangeConnectionNumber();

            }
        }

        public void ChangeConnectionNumber()
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (dispatcher.CheckAccess())
            {
                labelNum.Content = "Num. of connection: " + activeConnections.Count.ToString();
            }
            else
            {
                dispatcher.Invoke(() =>
                {
                    labelNum.Content = "Num. of connection: " + activeConnections.Count.ToString();
                });
            }
        }

        private void buttonCommandTest_Click(object sender, RoutedEventArgs e)
        {
            lock (lockObjectConnection)
            {
                commandList.Add("aggpointing\n");
            }
        }

        private void buttonCommandEnd_Click(object sender, RoutedEventArgs e)
        {
            lock (lockObjectConnection)
            {
                commandList.Add("end\n");
            }
        }

        private void buttonCommandSurprise_Click(object sender, RoutedEventArgs e)
        {
            lock (lockObjectConnection)
            {
                commandList.Add("surprise\n");
            }
        }

        private void buttonCommandPointing_Click(object sender, RoutedEventArgs e)
        {
            lock (lockObjectConnection)
            {
                commandList.Add("pointing\n");
            }
        }

        private void buttonCommandYeah_Click(object sender, RoutedEventArgs e)
        {
            lock (lockObjectConnection)
            {
                commandList.Add("yeah\n");
            }
        }

        private void buttonCommandStarepointing_Click(object sender, RoutedEventArgs e)
        {
            lock (lockObjectConnection)
            {
                commandList.Add("starepointing\n");
            }
        }

        private void buttonCommandSlippointing_Click(object sender, RoutedEventArgs e)
        {
            lock (lockObjectConnection)
            {
                commandList.Add("slippointing\n");
            }
        }

        private void buttonCommandStand_Click(object sender, RoutedEventArgs e)
        {
            lock (lockObjectConnection)
            {
                commandList.Add("test\n");
            }
        }

        private void buttonCommandAll_Click(object sender, RoutedEventArgs e)
        {
            lock (lockObjectConnection)
            {
                commandList.Add("all\n");
            }
        }

        private void buttonCommandWHsame_Click(object sender, RoutedEventArgs e)
        {
            lock (lockObjectConnection)
            {
                commandList.Add("WHsame\n");
            }
        }

        private void buttonCommandWHdiff_Click(object sender, RoutedEventArgs e)
        {
            lock (lockObjectConnection)
            {
                commandList.Add("WHdiff\n");
            }
        }

        private void buttonCommandAns_Click(object sender, RoutedEventArgs e)
        {
            lock (lockObjectConnection)
            {
                commandList.Add("ans\n");
            }
        }
    }
}
