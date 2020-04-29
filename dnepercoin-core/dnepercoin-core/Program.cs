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
using uhttpsharp;
using uhttpsharp.Listeners;
using uhttpsharp.RequestProviders;
using uhttpsharp.Headers;
using uhttpsharp.Handlers;
using uhttpsharp.Logging;

namespace dnepercoin_core
{
    class fix : IHttpRequestHandler
    {
        public async Task Handle(IHttpContext context, System.Func<Task> next)
        {
            string ach = Program.template;
            string data = "";
            foreach (var pair in Program.Balances)
            {
                data += "<tr><td>";
                data += BitConverter.ToString(pair.Key).Replace("-", string.Empty);
                data += "</td><td>";
                data += pair.Value;
                data += "</td></tr>";
            }
            ach = ach.Replace("{data}", data);

            ach += "Number of blocks: ";
            ach += Program.Blocks.Count;
            ach += "<br>Last block hash: 0x";
            ach += BitConverter.ToString(Program.LastBlockHash).Replace("-", string.Empty);
            ach += "<br>Number of confirmed transactions: ";
            ach += Program.TotalTransactions;
            ach += "<br>Number of transactions in swarm: ";
            ach += Program.Swarm.Count;
            ach += "<br>Difficulty: ";
            ach += Program.GlobalDifficulty;
            ach += "<br>Number of empty blocks: ";
            ach += Program.EmptyBlocks;
            ach += "<br>Block reward: ";
            ach += Block.GetBlockReward();

            context.Response = new HttpResponse(HttpResponseCode.Ok, Encoding.ASCII.GetBytes(ach), false);//new HttpResponse(HttpResponseCode.Ok, "text/html", ach, true);//context.Request.Headers.KeepAliveConnection());
        }
    }
    public class NullLoggerProvider : ILogProvider
    {
        public static readonly NullLoggerProvider Instance = new NullLoggerProvider();

        private static readonly ILog NullLogInstance = new NullLog();

        public ILog GetLogger(string name)
        {
            return NullLogInstance;
        }

        public IDisposable OpenNestedContext(string message)
        {
            return null;
        }

        public IDisposable OpenMappedContext(string key, string value)
        {
            return null;
        }

        public class NullLog : ILog
        {
            public bool Log(LogLevel logLevel, Func<string> messageFunc, Exception exception = null, params object[] formatParameters)
            {
                // do nothing
                return true;
            }
        }
    }
    class Program
    {
        public static uint GlobalDifficulty = 8, TotalTransactions = 0, EmptyBlocks = 0;
        public static byte[] LastBlockHash = new byte[32];
        public static Dictionary<byte[], double> Balances = new Dictionary<byte[], double>(new ByteArrayComparer());
        public static List<Block> Blocks = new List<Block>();
        public static List<Transaction> Swarm = new List<Transaction>();
        public static List<IPAddress> Clients = new List<IPAddress>();
        public static string template = File.ReadAllText("lul.html");
        public static bool isMining = false;
        public static List<Thread> miningThreads = new List<Thread>();
        public static byte[] pubKeyHash = new byte[20];
        public static ECDsaCng importedDSA;
        public static byte[] publicKey;
        static Program()
        {
            LogProvider.LogProviderResolvers.Clear();
            LogProvider.LogProviderResolvers.Add(
                new Tuple<LogProvider.IsLoggerAvailable, LogProvider.CreateLogProvider>(() => true,
                    () => NullLoggerProvider.Instance));
        }
        static void Main(string[] args)
        {

            //Clients.Add(IPAddress.Parse("192.168.43.174"));
            Clients.Add(IPAddress.Parse("192.168.43.153"));
            //Clients.Add(IPAddress.Parse("91.121.50.14"));
            Clients.Add(IPAddress.Parse("192.168.43.47"));
            //Clients.Add(IPAddress.Parse("192.168.43.41"));

            foreach (var ip in Discoverer.GetAllLocalIPv4())
            {
                Clients.Add(ip);
                Console.WriteLine("Local IP: " + ip.ToString());
            }

            Console.WriteLine("Starting node server...");

            Task.Run(() => Serve());

            Discoverer.PeerJoined = x => Connect(x);
            Discoverer.PeerLeft = x => Disconnect(x);
            Discoverer.Start();

            Task.Delay(1000).Wait();

            Task.Run(() => Update());

            Task.Delay(1000).Wait();

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
            importedDSA = new ECDsaCng(importedKey);

            publicKey = importedDSA.Key.Export(CngKeyBlobFormat.EccPublicBlob);

            using (SHA1 sha1 = SHA1.Create())
            {
                pubKeyHash = sha1.ComputeHash(publicKey);
            }

            Console.Write("Address: " + BitConverter.ToString(pubKeyHash).Replace("-", string.Empty));
            Console.WriteLine();


            var httpServer = new HttpServer(new HttpRequestProvider());
            var lhgdkg = new TcpListener(IPAddress.Any, 80);
            //lhgdkg.Server.ReceiveTimeout = 10000;
            httpServer.Use(new TcpListenerAdapter(lhgdkg));

            Thread abc = new Thread(_ => (new UI()).ShowDialog());
            abc.Start(null);
            
            /*httpServer.Use((a, b) => {

                string ach = Program.template;
                string data = "";
                foreach (var pair in Program.Balances)
                {
                    data += "<tr><td>";
                    data += BitConverter.ToString(pair.Key).Replace("-", string.Empty);
                    data += "</td><td>";
                    data += pair.Value;
                    data += "</td></tr>";
                }
                ach = ach.Replace("{data}", data);
                while (true)
                {
                    try
                    {
                        Thread.Sleep(100);
                        var tw = new StreamWriter("index.html");
                        tw.Write(ach);
                        tw.Close();
                        break;
                    }
                    catch { }
                }
                Thread.Sleep(100);
                //File.WriteAllText("index.html", ach);
                return b();
            });*/
            httpServer.Use(new fix());
            httpServer.Start();

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
                            Console.WriteLine("mine [threads] [address] : start/stop mining");
                            Console.WriteLine("bal [address] : view balance");
                            Console.WriteLine("exit : close the program");
                            break;
                        }
                    case "exit":
                        {
                            httpServer.Dispose();
                            Environment.Exit(0);
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
                            if (command.Length != 3)
                            {
                                Console.WriteLine("Missing arguments.");
                                break;
                            }
                            if (command[1].Length != 40)
                            {
                                Console.WriteLine("Invalid address.");
                                break;
                            }
                            byte[] addr = new byte[20];
                            int i = 0;
                            for (; i < 20; i++)
                            {
                                try
                                {
                                    addr[i] = Convert.ToByte(command[1].Substring(i * 2, 2), 16);
                                }
                                catch { Console.WriteLine("Invalid address."); break; }
                            }
                            if (i < 19)
                                break;
                            double amount = double.Parse(command[2]);

                            var tx = new Transaction();
                            tx.target = addr;
                            tx.amount = amount;
                            tx.source = publicKey;
                            tx.timestamp = (uint)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                            tx.signature = importedDSA.SignData(tx.GetBytesToSign(), HashAlgorithmName.SHA256);

                            IPEndPoint ipep2 = new IPEndPoint(IPAddress.Any, 53417);
                            UdpClient newsock = new UdpClient(ipep2);
                            newsock.Client.ReceiveTimeout = 1000;
                            newsock.Client.SendTimeout = 1000;

                            foreach (IPAddress ip in Clients)
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

                            Swarm.Add(tx);
                            break;
                        }
                    case "mine":
                        {
                            ulong threads = 1;
                            if (command.Length > 1)
                            {
                                threads = ulong.Parse(command[1]);
                            }
                            if (isMining)
                            {
                                Console.WriteLine("Stopped mining.");
                                miningThreads.ForEach(x => x.Abort());
                                miningThreads.Clear();
                            }
                            else
                            {
                                Console.WriteLine("Started mining.");
                                ulong nonceOffset = ulong.MaxValue / threads;
                                byte[] address = new byte[20];
                                if (command.Length > 2)
                                {
                                    for (int i = 0; i < 20; i++)
                                    {
                                        address[i] = Convert.ToByte(command[2].Substring(i * 2, 2), 16);
                                    }
                                }
                                else
                                    address = pubKeyHash;
                                Random r = new Random();
                                for (ulong i = 0; i < threads; i++)
                                {
                                    Thread miningThread = new Thread((param) => Mine(address, (ulong)param));
                                    byte[] nonce = new byte[8];
                                    r.NextBytes(nonce);
                                    nonce[7] = (byte)i;
                                    miningThread.Start(BitConverter.ToUInt64(nonce, 0));
                                    miningThreads.Add(miningThread);
                                }
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
            IPEndPoint ipep2 = new IPEndPoint(IPAddress.Any, 53419);
            UdpClient newsock = new UdpClient(ipep2);
            newsock.Client.ReceiveTimeout = 1000;
            newsock.Client.SendTimeout = 1000;
            foreach (IPAddress ip in Clients)
            {
                IPEndPoint ipep = new IPEndPoint(ip, 53418);
                byte[] data = null;
                try 
                { 
                    newsock.Send(new byte[] { 0x03 }, 1, ipep);
                }
                catch
                {
                    continue;
                }
                try 
                { 
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
            int j = 0;
            var stuff = Discoverer.GetAllLocalIPv4();
            while (true)
            {
                foreach (IPAddress ip in Clients)
                {
                    if (stuff.Contains(ip))
                        continue;
                    IPEndPoint ipep = new IPEndPoint(ip, 53418);
                    byte[] data = null;

                    try 
                    { 
                        newsock.Send(new byte[] { 0x00 }, 1, ipep);
                    }
                    catch
                    {
                        continue;
                    }

                    try 
                    { 
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
                                    LastBlockHash = Blocks[Blocks.Count - 1].previousBlockHash;
                                    var toBeReverted = Blocks[Blocks.Count - 1];
                                    foreach (var tx in toBeReverted.transactions)
                                    {
                                        byte[] pubKeyHash = new byte[20];
                                        using (SHA1 sha1 = SHA1.Create())
                                        {
                                            pubKeyHash = sha1.ComputeHash(tx.source);
                                        }
                                        Balances[pubKeyHash] += tx.amount;
                                        Balances[tx.target] -= tx.amount;
                                    }
                                    Balances[toBeReverted.rewardTarget] -= Block.GetBlockReward();
                                    if (toBeReverted.numTransactions == 0)
                                        EmptyBlocks--;
                                    else
                                        TotalTransactions -= (uint)toBeReverted.numTransactions;
                                    Blocks.Remove(toBeReverted);
                                    break;
                                }
                            }
                        }
                    }
                    Task.Delay(10).Wait();
                }

                Task.Delay(20).Wait();
                /*if (j < 30)
                    j++;
                else
                {
                    foreach (IPAddress ip in Clients)
                    {
                        IPEndPoint ipep = new IPEndPoint(ip, 53418);
                        byte[] data = null;

                        try
                        {
                            newsock.Send(new byte[] { 0x04 }, 1, ipep);
                        }
                        catch
                        {
                            continue;
                        }

                        try
                        {
                            data = newsock.Receive(ref ipep);
                        }
                        catch
                        {
                            continue;
                        }

                        if (data != null)
                        {
                            int count = BitConverter.ToInt32(data, 0);
                            for (int i = 0; i < count; i++)
                            {
                                byte[] iparr = new byte[4];
                                Array.Copy(data, 4 + (i * 4), iparr, 0, 4);
                                IPAddress ipadr = new IPAddress(iparr);
                                if (!Clients.Contains(ipadr))
                                    Clients.Add(ipadr);
                            }
                        }
                        Task.Delay(20).Wait();
                    }
                }*/
                //Task.Delay(500).Wait();
            }
        }
        static void Serve()
        {
            byte[] data = new byte[1024];
            IPEndPoint ipep = new IPEndPoint(IPAddress.Any, 53418);
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

                if (!Clients.Contains(sender.Address))
                    Clients.Add(sender.Address);

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
                    case 0x04: // Request clients
                        {
                            List<byte> packet = new List<byte>();
                            packet.AddRange(BitConverter.GetBytes(Clients.Count));
                            foreach (var tx in Clients)
                            {
                                packet.AddRange(tx.GetAddressBytes());
                            }
                            newsock.Send(packet.ToArray(), packet.Count, sender);
                            break;
                        }
                }
            }
        }

        static bool FastCheck(byte[] a, byte[] b, ulong nonce)
        {
            for (int i = 0; i < 30; i++)
            {
                ulong chk = (ulong)(1 << i);
                int c = (29 - i);
                ulong mod = nonce & (ulong)(0x3FFFFFFF >> c);
                if (mod != chk)
                {
                    if (a[c] != b[c])
                        return false;
                }
                else
                    return true;
            }
            return true;
        }

        public static void Mine(byte[] address, ulong startNonce)
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
                    block.nonce = startNonce;
                    while (block.nonce < ulong.MaxValue)
                    {
                        if (!FastCheck(LastBlockHash, block.previousBlockHash, block.nonce))//(!LastBlockHash.SequenceEqual(block.previousBlockHash))
                        {
                            if (startNonce == 0)
                            {
                                if (!Blocks[Blocks.Count - 1].rewardTarget.SequenceEqual(address))
                                    Console.Write("Someone else mined block...\n> ");
                            }
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
                            Console.Write("Previous balance: " + prev + " now: ");

                            foreach(var tx in block.transactions)
                                Swarm.Remove(tx);

                            var fin = new List<byte>();
                            fin.AddRange(header);
                            fin.AddRange(block.transactionData);
                            var blk = Block.FromBytes(fin.ToArray());
                            if (blk != null)
                            {
                                Blocks.Add(blk);
                                Console.WriteLine(Balances[address]);
                            }
                            else
                                Console.WriteLine("Invalidated block...");

                            Console.Write("> ");

                            break;
                        }
                        Thread.Sleep(10);
                        block.nonce++;
                    }
                }
            }
        }

        private static void Connect(IPAddress x)
        {
            if(!Clients.Contains(x))
                Clients.Add(x);
        }
    }
}
