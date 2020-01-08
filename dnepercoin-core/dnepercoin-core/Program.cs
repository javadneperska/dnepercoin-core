using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace dnepercoin_core
{
    class Program
    {
        public static uint GlobalDifficulty = 22;
        public static byte[] LastBlockHash = new byte[32];
        public static Dictionary<byte[], double> Balances = new Dictionary<byte[], double>(new ByteArrayComparer());
        public static List<Block> Blocks = new List<Block>();
        public static List<Transaction> Swarm = new List<Transaction>();
        public static List<IPAddress> Clients = new List<IPAddress>();
        static void Main(string[] args)
        {
            Clients.Add(IPAddress.Parse("192.168.43.174"));
            Clients.Add(IPAddress.Parse("192.168.43.47"));

            Console.WriteLine("Starting node server...");

            Task.Run(() => Serve());

            Discoverer.PeerJoined = x => Connect(x);
            Discoverer.PeerLeft = x => Disconnect(x);
            Discoverer.Start();

            Task.Delay(5000).Wait();

            Task.Run(() => Update());

            Task.Delay(5000).Wait();

            if (!File.Exists("privatekey.txt"))
            {
                Console.WriteLine("Generating key...");
                CngKeyCreationParameters keyCreationParameters = new CngKeyCreationParameters();
                keyCreationParameters.ExportPolicy = CngExportPolicies.AllowPlaintextExport;
                keyCreationParameters.KeyUsage = CngKeyUsages.Signing;

                CngKey key = CngKey.Create(CngAlgorithm.ECDsaP256, null, keyCreationParameters);

                ECDsaCng dsa = new ECDsaCng(key);
                byte[] privateKey = dsa.Key.Export(CngKeyBlobFormat.EccPrivateBlob);
                File.WriteAllText("privatekey.txt", String.Join(",", privateKey));
            }

            CngKey importedKey = CngKey.Import(File.ReadAllText("privatekey.txt").Split(',').Select(m => byte.Parse(m)).ToArray(), CngKeyBlobFormat.EccPrivateBlob);
            ECDsaCng importedDSA = new ECDsaCng(importedKey);

            byte[] publicKey = importedDSA.Key.Export(CngKeyBlobFormat.EccPublicBlob);

            byte[] pubKeyHash = new byte[20];
            using (SHA1 sha1 = SHA1.Create())
            {
                pubKeyHash = sha1.ComputeHash(publicKey);
            }

            Console.Write("Address: " + BitConverter.ToString(pubKeyHash).Replace("-", string.Empty));
            Console.WriteLine();

            bool isMining = false;
            Thread miningThread = null;

            while (true)
            {
                Console.Write("> ");
                string cmd = Console.ReadLine();
                string[] command = cmd.Split(' ');
                switch (command[0])
                {
                    case "help":
                        {
                            Console.WriteLine("help : view help");
                            Console.WriteLine("tx <address> <amount> : send money");
                            Console.WriteLine("mine : start/stop mining");
                            Console.WriteLine("bal [address] : view balance");
                            break;
                        }
                    case "bal":
                        {
                            byte[] address = new byte[20];
                            if (command.Length > 1)
                            {
                                for (int i = 0; i < 20; i++)
                                {
                                    address[i] = Convert.ToByte(command[1].Substring(i * 2, 2), 16);
                                }
                            }
                            else
                                address = pubKeyHash;
                            double balance = 0;
                            if (Balances.ContainsKey(address))
                                balance = Balances[address];
                            Console.WriteLine("Current balance: " + balance);
                            break;
                        }
                    case "tx":
                        {
                            if (command.Length != 3 || command[1].Length != 40)
                                break;
                            byte[] addr = new byte[20];
                            for (int i = 0; i < 20; i++)
                            {
                                addr[i] = Convert.ToByte(command[1].Substring(i * 2, 2), 16);
                            }
                            double amount = double.Parse(command[2]);

                            var tx = new Transaction();
                            tx.target = addr;
                            tx.amount = amount;
                            tx.source = publicKey;
                            tx.timestamp = (uint)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                            tx.signature = importedDSA.SignData(tx.GetBytesToSign(), HashAlgorithmName.SHA256);

                            IPEndPoint ipep2 = new IPEndPoint(IPAddress.Any, 6969);
                            UdpClient newsock = new UdpClient(ipep2);
                            newsock.Client.ReceiveTimeout = 1000;
                            newsock.Client.SendTimeout = 1000;

                            foreach (IPAddress ip in Clients)
                            {
                                IPEndPoint ipep = new IPEndPoint(ip, 9050);
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

                                try { 
                                data = newsock.Receive(ref ipep);
                                }
                                catch { continue; }
                            }

                            Swarm.Add(tx);
                            break;
                        }
                    case "mine":
                        {
                            if (isMining)
                            {
                                Console.WriteLine("Stopped mining.");
                                miningThread.Abort();
                            }
                            else
                            {
                                Console.WriteLine("Started mining.");
                                miningThread = new Thread(() => Mine(pubKeyHash));
                                miningThread.Start();
                            }
                            isMining = !isMining;
                            break;
                        }
                }
            }
        }

        private static void Disconnect(string x)
        {
            Clients.Remove(IPAddress.Parse(x));
        }

        static void Update()
        {
            Console.WriteLine("Running initial update...");
            IPEndPoint ipep2 = new IPEndPoint(IPAddress.Any, 8989);
            UdpClient newsock = new UdpClient(ipep2);
            newsock.Client.ReceiveTimeout = 1000;
            newsock.Client.SendTimeout = 1000;
            foreach (IPAddress ip in Clients)
            {
                IPEndPoint ipep = new IPEndPoint(ip, 9050);
                byte[] data = null;
                try { 
                newsock.Send(new byte[] { 0x03 }, 1, ipep);
                }
                catch
                {
                    continue;
                }
                try { 
                data = newsock.Receive(ref ipep);
                }
                catch
                {
                    continue;
                }

                if (data != null)
                {
                    int num = BitConverter.ToInt32(data, 0);
                    int index = 4;
                    for (int i = 0; i < num; i++)
                    {
                        ushort len = BitConverter.ToUInt16(data, index);
                        byte[] transaction = new byte[len];
                        Array.Copy(data, index + 4, transaction, 0, len);
                        var tx = Transaction.FromBytes(transaction, false);
                        if (!Swarm.Any(x => x.signature.SequenceEqual(tx.signature)))
                            Swarm.Add(tx);
                        index += 4 + len;
                    }
                }
            }
            while (true)
            {
                foreach (IPAddress ip in Clients)
                {
                    IPEndPoint ipep = new IPEndPoint(ip, 9050);
                    byte[] data = null;

                    try { 
                    newsock.Send(new byte[] { 0x00 }, 1, ipep);
                    }
                    catch
                    {
                        continue;
                    }

                    try { 
                    data = newsock.Receive(ref ipep);
                    }
                    catch
                    {
                        continue;
                    }

                    if (data != null)
                    {
                        byte[] hash = new byte[32];
                        Array.Copy(data, 0, hash, 0, 32);
                        if (!hash.SequenceEqual(LastBlockHash) && !Blocks.Any(x => x.previousBlockHash.SequenceEqual(hash)))
                        {
                            while (!LastBlockHash.SequenceEqual(hash))
                            {
                                List<byte> packet = new List<byte>();
                                packet.Add(0x01);
                                packet.AddRange(LastBlockHash);
                                newsock.Send(packet.ToArray(), packet.Count, ipep);
                                try
                                {
                                    data = newsock.Receive(ref ipep);
                                }
                                catch
                                {
                                    continue;
                                }
                                int len = BitConverter.ToInt32(data, 0);
                                if (len != 0)
                                {
                                    byte[] block = new byte[len];
                                    Array.Copy(data, 4, block, 0, len);
                                    var blk = Block.FromBytes(block);
                                    if (blk != null)
                                    {
                                        Blocks.Add(blk);
                                    }
                                    else
                                    {
                                        Console.WriteLine("Received invalid block");
                                        break;
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("Received invalid block hash");
                                    break;
                                }
                            }
                        }
                    }
                }

                Task.Delay(500).Wait();
            }
        }
        static void Serve()
        {
            byte[] data = new byte[1024];
            IPEndPoint ipep = new IPEndPoint(IPAddress.Any, 9050);
            UdpClient newsock = new UdpClient(ipep);
            newsock.Client.ReceiveTimeout = 1000;
            newsock.Client.SendTimeout = 1000;

            while (true)
            {
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);

                try
                {
                    data = newsock.Receive(ref sender);
                }
                catch
                {
                    continue;
                }

                switch (data[0])
                {
                    case 0x00: // Request Last Block Hash
                        {
                            if (LastBlockHash != null)
                                newsock.Send(LastBlockHash, 32, sender);
                            else
                            {
                                byte[] NullBlock = new byte[32];
                                newsock.Send(NullBlock, 32, sender);
                            }
                            break;
                        }
                    case 0x01: // Request Next Block
                        {
                            byte[] prevBlockHash = new byte[32];
                            Array.Copy(data, 1, prevBlockHash, 0, 32);
                            Block output = Blocks.FirstOrDefault(x => x.previousBlockHash.SequenceEqual(prevBlockHash));
                            if (output != null)
                            {
                                List<byte> packet = new List<byte>();
                                packet.AddRange(BitConverter.GetBytes(output.originalData.Length));
                                packet.AddRange(output.originalData);
                                newsock.Send(packet.ToArray(), packet.Count, sender);
                            }
                            else
                            {
                                byte[] NullBlock = new byte[4];
                                newsock.Send(NullBlock, 4, sender);
                            }
                            break;
                        }
                    case 0x02: // Add TX to swarm
                        {
                            ushort len = BitConverter.ToUInt16(data, 1);
                            byte[] transaction = new byte[len];
                            Array.Copy(data, 3, transaction, 0, len);
                            var tx = Transaction.FromBytes(transaction, false);
                            if (tx != null)
                            {
                                Swarm.Add(tx);
                                newsock.Send(new byte[] { 0x01 }, 1, sender);
                            }
                            else
                                newsock.Send(new byte[] { 0x00 }, 1, sender);
                            break;
                        }
                    case 0x03: // Request swarm
                        {
                            List<byte> packet = new List<byte>();
                            packet.AddRange(BitConverter.GetBytes(Swarm.Count));
                            foreach (var tx in Swarm)
                            {
                                packet.AddRange(BitConverter.GetBytes((ushort)tx.originalData.Length));
                                packet.AddRange(tx.originalData);
                            }
                            newsock.Send(packet.ToArray(), packet.Count, sender);
                            break;
                        }
                }
            }
        }

        static void Mine(byte[] address)
        {
            using (SHA256 sha = SHA256.Create())
            {
                while (true)
                {
                    var block = new Block();
                    block.previousBlockHash = LastBlockHash;
                    if (block.previousBlockHash == null)
                        block.previousBlockHash = new byte[32];
                    block.difficulty = GlobalDifficulty;
                    block.timestamp = (uint)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                    block.rewardTarget = address;
                    block.transactions.AddRange(Swarm);
                    block.SetupTransactionHash();
                    block.nonce = 0;
                    while (block.nonce < ulong.MaxValue)
                    {
                        if (!LastBlockHash.SequenceEqual(block.previousBlockHash))
                        {
                            Console.Write("Someone else mined block...\n> ");
                            break;
                        }

                        var header = block.CreateHeader();
                        var hash = sha.ComputeHash(header);

                        bool good = true;
                        for (int i = 0; i < block.difficulty; i++)
                        {
                            int b = 0, j = i;
                            while (j > 7)
                            {
                                b++;
                                j -= 8;
                            }

                            if ((hash[b] & (1 << (7 - j))) != 0)
                                good = false;
                        }
                        if (good)
                        {
                            Console.Write("Found block - hash: 0x");                            
                            Console.WriteLine(BitConverter.ToString(hash).Replace("-", string.Empty));
                            double prev = 0;
                            if (Balances.ContainsKey(address))
                                prev = Balances[address];
                            Console.WriteLine("Previous balance: " + prev + " now: " + (prev + Block.GetBlockReward()));

                            foreach(var tx in block.transactions)
                                Swarm.Remove(tx);

                            var fin = new List<byte>();
                            fin.AddRange(header);
                            fin.AddRange(block.transactionData);
                            var blk = Block.FromBytes(fin.ToArray());
                            if (blk != null)
                            {
                                Blocks.Add(blk);
                            }
                            else
                                Console.WriteLine("Invalidated block...");

                            Console.Write("> ");

                            break;
                        }

                        block.nonce++;
                    }
                }
            }
        }

        private static void Connect(IPAddress x)
        {
            Clients.Add(x);
        }
    }
}
