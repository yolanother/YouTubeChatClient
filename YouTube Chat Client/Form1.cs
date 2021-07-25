using Google.Apis.YouTube.v3.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using YouTube.Base;
using YouTube.Base.Clients;

namespace YouTube_Chat_Client
{
    public partial class Form1 : Form
    {

        private static SemaphoreSlim fileLock = new SemaphoreSlim(1);
        private YouTubeConnection connection;
        private ChatClient client;
        private Channel channel;
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

        public Form1()
        {
            InitializeComponent();
            ShowInTaskbar = false;
            txtClientId.Text = Properties.Settings.Default.clientId;
            txtClientSecret.Text = Properties.Settings.Default.clientSecret;
            txtChannel.Text = Properties.Settings.Default.channel;
        }

        private void Form1_ResizeEnd(object sender, EventArgs e)
        {

        }

        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            WindowState = FormWindowState.Normal;
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
                ConnectToChannel("UCWxlUwW9BgGISaakjGM37aw");
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
                //

                //Channel channel = await connection.Channels.GetChannelByID("UCyl1z3jo3XHR1riLFKG5UAg");


                if (channel != null)
                {
                    this.channel = channel;
                    Log("Connection successful. Joined channel: " + channel.Snippet.Title);

                    //LiveBroadcast broadcast = await connection.LiveBroadcasts.GetChannelActiveBroadcast(channel);
                    //if (broadcast == null)
                    //{
                    //    broadcast = await connection.LiveBroadcasts.GetBroadcastByID("9rCRhTrEpDE");
                    //    if (broadcast == null)
                    //    {
                    //        broadcast = new LiveBroadcast() { Snippet = new LiveBroadcastSnippet() { LiveChatId = "Cg0KC1VxWFFjZmZvTXhjKicKGFVDSHN4NEhxYS0xT1JqUVRoOVRZRGh3dxILVXFYUWNmZm9NeGM" } };
                    //    }
                    //}

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
                        foreach(var message in messages.Messages)
                        {
                            Log(string.Format("{0}: {1}", message.AuthorDetails.DisplayName, message.Snippet.DisplayMessage));
                        }
                    }
                }
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

                    connection = await YouTubeConnection.ConnectViaLocalhostOAuthBrowser(txtClientId.Text, txtClientSecret.Text, scopes);
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
                btnLogin.Visible = false;
                btnJoin.Visible = true;
            }
        }

        private void Client_OnMessagesReceived(object sender, IEnumerable<LiveChatMessage> e)
        {
            foreach(var message in e)
            {
                Log(string.Format("{0}: {1}", message.AuthorDetails.DisplayName, message.Snippet.DisplayMessage));
            }
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
            if (null == channel || null == connection) return;

            new Task(async () =>
            {
                var broadcast = await connection.LiveBroadcasts.GetChannelActiveBroadcast(channel);

                if (await connection.LiveBroadcasts.GetMyActiveBroadcast() != null)
                {
                    await client.SendMessage(message);
                }
            });
        }
    }
}
