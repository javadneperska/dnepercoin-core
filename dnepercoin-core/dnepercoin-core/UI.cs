using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace dnepercoin_core
{
    public partial class UI : Form
    {
        public UI()
        {
            InitializeComponent();
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            ulong threads = 4;
            if (Program.isMining)
            {
                Console.WriteLine("Stopped mining.");
                Program.miningThreads.ForEach(x => x.Abort());
                Program.miningThreads.Clear();
            }
            else
            {
                Console.WriteLine("Started mining.");
                ulong nonceOffset = ulong.MaxValue / threads;
                byte[] address = new byte[20];
                address = Program.pubKeyHash;
                Random r = new Random();
                for (ulong i = 0; i < threads; i++)
                {
                    Thread miningThread = new Thread((param) => Program.Mine(address, (ulong)param));
                    byte[] nonce = new byte[8];
                    r.NextBytes(nonce);
                    nonce[7] = (byte)i;
                    miningThread.Start(BitConverter.ToUInt64(nonce, 0));
                    Program.miningThreads.Add(miningThread);
                }
            }
            Program.isMining = !Program.isMining;
            button1.Text = Program.isMining ? "Stop Mining" : "Start Mining";
        }

        private void button2_Click(object sender, EventArgs e)
        {
            byte[] addr = new byte[20];
            int i = 0;
            for (; i < 20; i++)
            {
                try
                {
                    addr[i] = Convert.ToByte(textBox1.Text.Substring(i * 2, 2), 16);
                }
                catch { Console.WriteLine("Invalid address."); break; }
            }
            if (i < 19)
                return;
            double amount = double.Parse(textBox2.Text);
            var tx = new Transaction();
            tx.target = addr;
            tx.amount = amount;
            tx.source = Program.publicKey;
            tx.timestamp = (uint)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
            tx.signature = Program.importedDSA.SignData(tx.GetBytesToSign(), HashAlgorithmName.SHA256);

            IPEndPoint ipep2 = new IPEndPoint(IPAddress.Any, 53417);
            UdpClient newsock = new UdpClient(ipep2);
            newsock.Client.ReceiveTimeout = 1000;
            newsock.Client.SendTimeout = 1000;

            foreach (IPAddress ip in Program.Clients)
            {
                if (Discoverer.GetAllLocalIPv4().Contains(ip))
                    continue;

                IPEndPoint ipep = new IPEndPoint(ip, 53418);
                byte[] data = null;

                byte[] transaction = tx.GetBytesTotal();

                List<byte> packet = new List<byte>();
                packet.Add(0x02);
                packet.AddRange(BitConverter.GetBytes((ushort)transaction.Length));
                packet.AddRange(transaction);
                try
                {
                    newsock.Send(packet.ToArray(), packet.Count, ipep);
                }
                catch { continue; }

                try
                {
                    data = newsock.Receive(ref ipep);
                }
                catch { continue; }
            }

            newsock.Close();

            Program.Swarm.Add(tx);
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (Program.Balances.ContainsKey(Program.pubKeyHash)) { 
            double balance = Program.Balances[Program.pubKeyHash];
            label1.Text = ("Current balance: " + balance);
        }
        }
    }
}
