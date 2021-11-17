using Google.Apis.YouTube.v3.Data;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using UdpBroadcastUtility;
using YouTube.Base;
using YouTube.Base.Clients;
using Settings = YouTube_Chat_Client.Properties.Settings;

namespace YouTube_Chat_Client
{
    public partial class Form1 : Form
    {

        private static SemaphoreSlim fileLock = new SemaphoreSlim(1);
        private YouTubeConnection connection;
        private ChatClient client;
        private Channel channel;
        private NamedPipeServerStream pipeServer;
        private StreamWriter pipeStream;
        private NamedPipeClientStream pipeClient;
        public static readonly List<OAuthClientScopeEnum> scopes = new List<OAuthClientScopeEnum>()
        {
            OAuthClientScopeEnum.ChannelMemberships,
            OAuthClientScopeEnum.ManageAccount,
            OAuthClientScopeEnum.ManageData,
            OAuthClientScopeEnum.ManagePartner,
            OAuthClientScopeEnum.ManagePartnerAudit,
            OAuthClientScopeEnum.ManageVideos,
            OAuthClientScopeEnum.ReadOnlyAccount,
            OAuthClientScopeEnum.ViewAnalytics,
            OAuthClientScopeEnum.ViewMonetaryAnalytics
        };

        public string Hostname
        {
            get
            {
                return Settings.Default.hostname;
            }

            set
            {
                Settings.Default.hostname = value;
                Settings.Default.Save();
            }
        }

        public int PortStart
        {
            get
            {
                return Settings.Default.startPort;
            }

            set
            {
                Settings.Default.startPort = value;
                Settings.Default.Save();
            }
        }
        public int PortEnd
        {
            get
            {
                return Settings.Default.endPort;
            }

            set
            {
                Settings.Default.endPort = value;
                Settings.Default.Save();
            }
        }

        public byte PartialTranscriptionDataType
        {
            get
            {
                return Settings.Default.dataType;
            }

            set
            {
                Settings.Default.dataType = value;
                Settings.Default.Save();
            }
        }

        public Form1()
        {
            InitializeComponent();
            ShowInTaskbar = false;
            txtClientId.Text = Properties.Settings.Default.clientId;
            txtClientSecret.Text = Properties.Settings.Default.clientSecret;
            txtChannel.Text = Properties.Settings.Default.channel;
            txtNamedPipe.Text = Settings.Default.namedPipe;
            SetTextEventDelegate = SetText;
        }

        private void SetText(string text)
        {
            txtPipe.Text = text + "\r\n" + txtPipe.Text;
        }

        private void Form1_ResizeEnd(object sender, EventArgs e)
        {
            //TopMost = WindowState == FormWindowState.Normal;
            //if (WindowState == FormWindowState.Minimized) Hide();
        }

        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            WindowState = FormWindowState.Normal;
            Focus();
            BringToFront();
        }

        private void btnLogin_Click(object sender, EventArgs e)
        {
            Login();
        }

        private void ConnectToChannel()
        {
            if (null == connection) return;

            if (string.IsNullOrEmpty(txtChannel.Text))
            {
                Task.Run(async () =>
                {
                    Channel channel = await connection.Channels.GetMyChannel();
                    ConnectToChannel(channel);
                });
            }
            else
            {
                ConnectToChannel(txtChannel.Text);
            }
        }

        private void ConnectToChannel(string channel)
        {
            if (null == connection) return;

            Task.Run(async () =>
            {
                Channel c = await connection.Channels.GetChannelByID(channel);
                ConnectToChannel(c);
            });
        }

        private async void ConnectToChannel(Channel channel)
        {
            if (connection != null)
            {

                if (channel != null)
                {
                    Log("Connection successful. Joined channel: " + channel.Snippet.Title);
                    this.channel = channel;

                    client = new ChatClient(connection);
                    client.OnMessagesReceived += Client_OnMessagesReceived;
                    if (await client.Connect())
                    {
                        Log("Live chat connection successful!");
                    }
                    else
                    {
                        Log("Failed to connect to live chat");
                    }


                    var broadcast = await connection.LiveBroadcasts.GetChannelActiveBroadcast(channel);
                    if (null != broadcast)
                    {
                        var messages = await connection.LiveChat.GetMessages(broadcast);
                        foreach (var message in messages.Messages)
                        {
                            Log(string.Format("{0}: {1}", message.AuthorDetails.DisplayName, message.Snippet.DisplayMessage));
                        }
                    }
                }
                OnConnectionStateChanged();
            }
        }

        private void Login()
        {
            Properties.Settings.Default.clientId = txtClientId.Text;
            Properties.Settings.Default.clientSecret = txtClientSecret.Text;
            Properties.Settings.Default.Save();
            Task.Run(async () =>
            {
                try
                {
                    Log("Initializing connection");

                    if (!string.IsNullOrEmpty(Properties.Settings.Default.token))
                    {
                        try
                        {
                            connection = await YouTubeConnection.ConnectViaAuthorizationCode(txtClientId.Text, txtClientSecret.Text, Properties.Settings.Default.token);
                        }
                        catch (Exception ex)
                        {
                            Log(ex.Message + "\r\nCould not restore login. Requesting a new login.");
                        }
                    }

                    if(null == connection)
                    {
                        connection = await YouTubeConnection.ConnectViaLocalhostOAuthBrowser(txtClientId.Text, txtClientSecret.Text, scopes);
                        var token = connection.GetOAuthTokenCopy();
                        Properties.Settings.Default.token = token.authorizationCode;
                        Properties.Settings.Default.Save();
                    }
                    OnConnectionStateChanged();
                }
                catch (Exception ex)
                {
                    Log(ex.Message);
                }
            });
        }

        private delegate void SafeCallVoidDelegate();
        private void OnConnectionStateChanged()
        {
            if (btnLogin.InvokeRequired)
            {
                btnLogin.Invoke(new SafeCallVoidDelegate(OnConnectionStateChanged));
            }
            else
            {
                btnLogin.Visible = null == connection;
                btnJoin.Visible = btnLogin.Visible == false && null == channel;
                btnSend.Visible = btnJoin.Visible == false && null != channel;
                txtChatMessage.Visible = btnSend.Visible;
            }
        }

        private void Client_OnMessagesReceived(object sender, IEnumerable<LiveChatMessage> e)
        {
            foreach(var message in e)
            {
                Log(string.Format("{0}: {1}", message.AuthorDetails.DisplayName, message.Snippet.DisplayMessage));
                
                string serializedString = JsonConvert.SerializeObject(message);

                Send(serializedString);
            }
        }

        private void Send(string serializedString)
        {
            Broadcaster.Send(serializedString, 6, Hostname, PortStart, PortEnd);
            WriteNamedPipe(serializedString);
        }

        public delegate void SetTextEvent(string text);
        public SetTextEvent SetTextEventDelegate;

        private void WriteNamedPipe(string serializedString)
        {
            if(!string.IsNullOrEmpty(Settings.Default.namedPipe))
            {
                if(null == pipeServer)
                {
                    pipeServer = new NamedPipeServerStream(Settings.Default.namedPipe, PipeDirection.Out);
                    pipeServer.WaitForConnectionAsync();
                    pipeStream = new StreamWriter(pipeServer);
                    pipeClient = new NamedPipeClientStream(".", Settings.Default.namedPipe, PipeDirection.In);
                    pipeClient.Connect();
                    new Thread(() =>
                    {
                        using (StreamReader sr = new StreamReader(pipeClient))
                        {
                            string temp;
                            while ((temp = sr.ReadLine()) != null)
                            {
                                txtPipe.Invoke(SetTextEventDelegate, temp);
                            }
                        }
                    }).Start();
                }

                if (pipeServer.IsConnected)
                {
                    pipeStream.WriteLine(serializedString);
                    pipeStream.Flush();
                }
            }
        }

        private void PipeServerWorker()
        {
            
        }

        private delegate void SafeCallDelegate(string text);
        private void Log(string message)
        {
            if(txtLog.InvokeRequired)
            {
                var d = new SafeCallDelegate(Log);
                txtLog.Invoke(d, new object[] { message });
            }
            else
            {
                txtLog.Text += message + "\r\n";
            }
        }

        private void txtChannel_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.channel = txtChannel.Text;
            Properties.Settings.Default.Save();
        }

        private void btnJoin_Click(object sender, EventArgs e)
        {
            ConnectToChannel();
        }

        public void SendMessage(string message)
        {
            /*if (null == channel || null == connection) return;

            Task.Run(async () =>
            {
                var broadcast = await connection.LiveBroadcasts.GetChannelActiveBroadcast(channel);

                if (await connection.LiveBroadcasts.GetMyActiveBroadcast() != null)
                {
                    await client.SendMessage(message);
                }
            });*/
            Broadcaster.Send(message, 4, Hostname, PortStart, PortEnd);
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            SendMessage(txtChatMessage.Text);
        }

        private void txtChatMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) SendMessage(txtChatMessage.Text);
        }

        private void txtNamedPipe_TextChanged(object sender, EventArgs e)
        {
            Settings.Default.namedPipe = txtNamedPipe.Text;
            Settings.Default.Save();
        }

        private void btnSendDebug_Click(object sender, EventArgs e)
        {
            Send(txtDebug.Text);
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            var result = savePipeFileDialog.ShowDialog();
            if(result == DialogResult.OK)
            {
                txtNamedPipe.Text = savePipeFileDialog.FileName;
                Settings.Default.namedPipe = txtNamedPipe.Text;
                Settings.Default.Save();
            }
        }

        private void txtClientId_TextChanged(object sender, EventArgs e)
        {

            Properties.Settings.Default.clientId = txtClientId.Text;
            Settings.Default.Save();
        }

        private void txtClientSecret_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.clientSecret = txtClientSecret.Text;
            Settings.Default.Save();
        }
    }
}
