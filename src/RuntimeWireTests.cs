using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using HarmonyLib;

namespace ValheimWhiteboard
{
    // Opt-in disposable headless QA only. Uses real game sockets, framing, routing and
    // typed delegates. This is not a Steam-authenticated vanilla-client join test.
    internal static class RuntimeWireTests
    {
        public static void Run(Action<bool, string> check)
        {
            var originalRouter = ZRoutedRpc.instance;
            var singleton = AccessTools.Field(typeof(ZRoutedRpc), "s_instance");
            var peers = (List<ZNetPeer>)AccessTools.Field(typeof(ZNet), "m_peers").GetValue(ZNet.instance);
            var cleanup = new List<ZNetPeer>();
            var host = new ZRoutedRpc(true); host.SetUID(ZNet.GetUID());
            var modded = new ZRoutedRpc(false); modded.SetUID(long.MaxValue - 1);
            var vanilla = new ZRoutedRpc(false); vanilla.SetUID(long.MaxValue - 2);
            singleton.SetValue(null, host);
            try
            {
                ZNetPeer[] Pair(long clientId)
                {
                    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                    try
                    {
                        var client = new TcpClient(); client.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                        var accepted = listener.AcceptTcpClient();
                        var a = new ZNetPeer(new ZSocket2(accepted, "localhost"), false) { m_uid = clientId };
                        var b = new ZNetPeer(new ZSocket2(client, "localhost"), true) { m_uid = ZNet.GetUID() };
                        cleanup.Add(a); cleanup.Add(b); return new[] {a,b};
                    }
                    finally { listener.Stop(); }
                }
                var writer = Pair(long.MaxValue - 1); var stock = Pair(long.MaxValue - 2);
                peers.Add(writer[0]); peers.Add(stock[0]);
                host.AddPeer(writer[0]); modded.AddPeer(writer[1]);
                host.AddPeer(stock[0]); vanilla.AddPeer(stock[1]);
                BoardNetwork.Register();
                bool hello = false, rejected = false, vanillaData = false;
                int stockRoutedMessages = 0;
                stock[1].m_rpc.Register<ZPackage>("RoutedRPC", (rpc, package) => stockRoutedMessages++);
                modded.Register<int>(BoardNetwork.HelloResult, (sender, protocol) => hello = sender == ZNet.GetUID() && protocol == BoardNetwork.Protocol);
                var missing = new ZDOID(ZNet.GetUID(), uint.MaxValue);
                modded.Register<ZDOID,int,bool,int,string>(BoardNetwork.Result, (sender,id,request,accepted,revision,pixels) =>
                    rejected = sender == ZNet.GetUID() && id.Equals(missing) && request == 77 && !accepted && revision == 0 && pixels == BoardSchema.Blank);
                void Pump(Func<bool> done)
                {
                    var timer = Stopwatch.StartNew();
                    while (!done() && timer.ElapsedMilliseconds < 5000)
                    { foreach (var peer in cleanup) peer.m_rpc.Update(.01f); Thread.Sleep(2); }
                }
                modded.InvokeRoutedRPC(ZNet.GetUID(), BoardNetwork.Hello, BoardNetwork.Protocol);
                Pump(() => hello);
                check(hello, "real TCP/ZRpc/ZRoutedRpc capability handshake reaches production handler");
                modded.InvokeRoutedRPC(ZNet.GetUID(), BoardNetwork.Save, missing, 77, 0, BoardSchema.Blank);
                Pump(() => rejected);
                check(rejected, "production missing-board rejection survives actual five-argument wire acknowledgement");
                // A stock endpoint has no Chalkboard handlers. Its ordinary ZDO payload still works.
                stock[1].m_rpc.Register<ZPackage>("ChalkboardQA_StockData", (rpc, package) =>
                { vanillaData = package.ReadInt() == BoardSchema.SignHash && package.ReadString() == "Chalkboard"; });
                var data = new ZPackage(); data.Write(BoardSchema.SignHash); data.Write("Chalkboard");
                stock[0].m_rpc.Invoke("ChalkboardQA_StockData", data);
                Pump(() => vanillaData);
                check(vanillaData, "stock endpoint without chalkboard handlers receives ordinary sign data over real socket");
                check(stockRoutedMessages == 0, "targeted handshake/save replies do not arrive at the stock socket");
            }
            finally
            {
                foreach (var peer in cleanup) { peers.Remove(peer); peer.m_rpc.Dispose(); }
                singleton.SetValue(null, originalRouter);
                AccessTools.Field(typeof(BoardNetwork), "router").SetValue(null, originalRouter);
            }
        }
    }
}
